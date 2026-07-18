using System.Globalization;
using System.Text;
using Knowledge.Domain.Ishikawa;
using Npgsql;

namespace Knowledge.Api;

/// <summary>Resultado da busca semântica de causa raiz: causa + score de cosseno.</summary>
public sealed record CausaRaizHit(RootCause Causa, double Score);

/// <summary>
/// Persistência da base de causa raiz (épico #7, 2º push): tabela ESTRUTURADA
/// (ativo/categoria/confiança — é o que a inferência lógica do épico #10 encadeia)
/// + embedding pgvector do sintoma (é o que o RAG do iDMSS busca por semântica).
/// Upsert idempotente por (ativo_id, sintoma): re-semear no boot nunca duplica nem
/// rebaixa confiança já validada pela operação.
/// </summary>
public sealed class CausaRaizStore(string connectionString, int dimensions)
{
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $$"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS causa_raiz (
                causa_id UUID PRIMARY KEY,
                ativo_id TEXT NOT NULL,
                categoria TEXT NOT NULL,
                sintoma TEXT NOT NULL,
                causa TEXT NOT NULL,
                motivo_codigo TEXT NULL,
                confianca DOUBLE PRECISION NOT NULL,
                registrado_em TIMESTAMPTZ NOT NULL,
                embedding vector({{dimensions}}) NOT NULL,
                UNIQUE (ativo_id, sintoma)
            );
            CREATE INDEX IF NOT EXISTS causa_raiz_ativo ON causa_raiz (ativo_id, categoria);
            CREATE INDEX IF NOT EXISTS causa_raiz_embedding ON causa_raiz
                USING hnsw (embedding vector_cosine_ops);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Grava uma causa. Seed (curada=false) nunca sobrescreve o que já existe;
    /// registro curado (curada=true, vindo da elicitação com a operação) atualiza
    /// causa, categoria e confiança.
    /// </summary>
    public async Task<Guid> UpsertAsync(RootCause causa, float[] embedding, bool curada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(causa);
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var conflict = curada
            ? """
              ON CONFLICT (ativo_id, sintoma) DO UPDATE SET
                  categoria = EXCLUDED.categoria, causa = EXCLUDED.causa,
                  motivo_codigo = EXCLUDED.motivo_codigo, confianca = EXCLUDED.confianca,
                  registrado_em = EXCLUDED.registrado_em, embedding = EXCLUDED.embedding
              """
            : "ON CONFLICT (ativo_id, sintoma) DO NOTHING";

        await using var cmd = new NpgsqlCommand(
            $"""
            INSERT INTO causa_raiz
                (causa_id, ativo_id, categoria, sintoma, causa, motivo_codigo, confianca, registrado_em, embedding)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9::vector)
            {conflict}
            RETURNING causa_id
            """, conn);
        cmd.Parameters.AddWithValue(causa.CausaId);
        cmd.Parameters.AddWithValue(causa.AtivoId);
        cmd.Parameters.AddWithValue(causa.Categoria.ToString());
        cmd.Parameters.AddWithValue(causa.Sintoma);
        cmd.Parameters.AddWithValue(causa.Causa);
        cmd.Parameters.AddWithValue((object?)causa.MotivoCodigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue(causa.Confianca);
        cmd.Parameters.AddWithValue(causa.RegistradoEm);
        cmd.Parameters.AddWithValue(ToVectorLiteral(embedding));

        // DO NOTHING não devolve linha: o id retornado é o EXISTENTE nesse caso.
        var returned = await cmd.ExecuteScalarAsync(ct);
        if (returned is Guid id)
            return id;

        await using var lookup = new NpgsqlCommand(
            "SELECT causa_id FROM causa_raiz WHERE ativo_id = $1 AND sintoma = $2", conn);
        lookup.Parameters.AddWithValue(causa.AtivoId);
        lookup.Parameters.AddWithValue(causa.Sintoma);
        return (Guid)(await lookup.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Consulta estruturada: por ativo e (opcional) categoria, mais confiável primeiro.</summary>
    public async Task<IReadOnlyList<RootCause>> QueryAsync(
        string ativoId, IshikawaCategory? categoria, int limit, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT causa_id, ativo_id, categoria, sintoma, causa, motivo_codigo, confianca, registrado_em
            FROM causa_raiz
            WHERE ativo_id = $1 AND ($2::text IS NULL OR categoria = $2)
            ORDER BY confianca DESC, registrado_em DESC
            LIMIT $3
            """, conn);
        cmd.Parameters.AddWithValue(ativoId);
        cmd.Parameters.AddWithValue((object?)categoria?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        return await ReadCausasAsync(cmd, ct);
    }

    /// <summary>Busca semântica: sintoma novo → causas de sintomas parecidos (qualquer ativo ou um só).</summary>
    public async Task<IReadOnlyList<CausaRaizHit>> SearchAsync(
        float[] queryEmbedding, string? ativoId, int limit, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT causa_id, ativo_id, categoria, sintoma, causa, motivo_codigo, confianca, registrado_em,
                   1 - (embedding <=> $1::vector) AS score
            FROM causa_raiz
            WHERE $2::text IS NULL OR ativo_id = $2
            ORDER BY embedding <=> $1::vector
            LIMIT $3
            """, conn);
        cmd.Parameters.AddWithValue(ToVectorLiteral(queryEmbedding));
        cmd.Parameters.AddWithValue((object?)ativoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        var hits = new List<CausaRaizHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new CausaRaizHit(MapCausa(reader), reader.GetDouble(8)));
        return hits;
    }

    private static async Task<IReadOnlyList<RootCause>> ReadCausasAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var causas = new List<RootCause>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            causas.Add(MapCausa(reader));
        return causas;
    }

    private static RootCause MapCausa(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        Enum.Parse<IshikawaCategory>(reader.GetString(2)),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetDouble(6),
        reader.GetFieldValue<DateTimeOffset>(7));

    private static string ToVectorLiteral(float[] embedding)
    {
        var sb = new StringBuilder(embedding.Length * 12).Append('[');
        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(embedding[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }
}
