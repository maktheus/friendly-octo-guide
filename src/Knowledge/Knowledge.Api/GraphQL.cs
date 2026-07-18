using System.Security.Claims;
using System.Text.Json;
using Knowledge.Domain.Chunking;
using Knowledge.Domain.Embeddings;
using Knowledge.Domain.Ishikawa;

namespace Knowledge.Api;

/// <summary>Consultas GraphQL: busca semântica filtrada pelo RBAC do chamador.</summary>
public sealed class Query
{
    /// <summary>Busca por similaridade de cosseno sobre o índice pgvector.</summary>
    public async Task<IReadOnlyList<SearchHit>> Search(
        string query, int limit, ClaimsPrincipal user,
        KnowledgeStore store, IEmbedder embedder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var bounded = Math.Clamp(limit, 1, 50);
        var embedding = await embedder.EmbedAsync(query, ct);
        return await store.SearchAsync(embedding, RolesOf(user), bounded, ct);
    }

    /// <summary>Base de causa raiz de um ativo (Ishikawa 6M), mais confiável primeiro.</summary>
    public async Task<IReadOnlyList<RootCause>> CausasRaiz(
        string ativoId, IshikawaCategory? categoria, int limit,
        CausaRaizStore causas, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ativoId);
        return await causas.QueryAsync(ativoId, categoria, Math.Clamp(limit, 1, 100), ct);
    }

    /// <summary>
    /// Diagnóstico por sintoma: busca semântica na base de causa raiz — sintoma novo
    /// encontra causas de sintomas parecidos. É a consulta que o iDMSS (épico #8) e a
    /// inferência lógica (épico #10) usam como evidência.
    /// </summary>
    public async Task<IReadOnlyList<CausaRaizHit>> DiagnosticoPorSintoma(
        string sintoma, string? ativoId, int limit,
        CausaRaizStore causas, IEmbedder embedder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sintoma);
        var embedding = await embedder.EmbedAsync(sintoma, ct);
        return await causas.SearchAsync(embedding, ativoId, Math.Clamp(limit, 1, 20), ct);
    }

    internal static List<string> RolesOf(ClaimsPrincipal user) =>
        [.. user.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value)];
}

/// <summary>Mutações GraphQL: ingestão/reindexação de documentos.</summary>
public sealed class Mutation
{
    /// <summary>
    /// Indexa um documento: divide em chunks, gera embeddings e grava tudo numa
    /// transação. Idempotente por id — reenviar reindexa.
    /// </summary>
    public async Task<Guid> IngestDocument(
        Guid? id, string title, string content, IReadOnlyList<string>? visibleToRoles,
        KnowledgeStore store, IEmbedder embedder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var documentId = id ?? Guid.NewGuid();
        var chunks = DocumentChunker.Split(content);

        var embedded = new List<(Chunk, float[])>(chunks.Count);
        foreach (var chunk in chunks)
            embedded.Add((chunk, await embedder.EmbedAsync(chunk.Content, ct)));

        var metadata = JsonSerializer.Serialize(new { chunkCount = chunks.Count, chars = content.Length });
        await store.IndexAsync(documentId, title, metadata, visibleToRoles ?? [], embedded, ct);
        return documentId;
    }

    /// <summary>
    /// Registra (ou corrige) uma causa raiz curada — o caminho da elicitação com a
    /// operação (workshops de Ishikawa). Curada SEMPRE vence a hipótese do seed.
    /// Sem categoria explícita, o classificador 6M decide pelo sintoma.
    /// </summary>
    public async Task<Guid> RegistrarCausaRaiz(
        string ativoId, string sintoma, string causa,
        IshikawaCategory? categoria, string? motivoCodigo, double confianca,
        CausaRaizStore causas, IEmbedder embedder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ativoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sintoma);
        ArgumentException.ThrowIfNullOrWhiteSpace(causa);

        var registro = new RootCause(
            Guid.NewGuid(), ativoId,
            categoria ?? IshikawaClassifier.Classify(sintoma),
            sintoma.Trim(), causa.Trim(), motivoCodigo,
            Math.Clamp(confianca, 0, 1), DateTimeOffset.UtcNow);

        var embedding = await embedder.EmbedAsync(registro.Sintoma, ct);
        return await causas.UpsertAsync(registro, embedding, curada: true, ct);
    }
}
