using Npgsql;

namespace Mes.Connector.Worker;

/// <summary>Persistência do cursor de polling: restart NÃO re-polla o MES inteiro.</summary>
public interface ICursorStore
{
    Task<string?> LoadAsync(string sourceSystem, CancellationToken ct);
    Task SaveAsync(string sourceSystem, string cursor, CancellationToken ct);
}

/// <summary>Fase 0 (sem Postgres configurado): cursor vive e morre com o processo.</summary>
public sealed class InMemoryCursorStore : ICursorStore
{
    private string? _cursor;
    public Task<string?> LoadAsync(string sourceSystem, CancellationToken ct) => Task.FromResult(_cursor);

    public Task SaveAsync(string sourceSystem, string cursor, CancellationToken ct)
    {
        _cursor = cursor;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Cursor durável em Postgres (épico #6, fase 1): uma linha por sistema de origem.
/// Upsert idempotente — duas réplicas do mesmo source não corrompem (a última vence;
/// o consumidor deduplica por event_id de qualquer forma).
/// </summary>
public sealed class PostgresCursorStore(string connectionString) : ICursorStore
{
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS mes_cursor (
                source_system TEXT PRIMARY KEY,
                last_cursor TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> LoadAsync(string sourceSystem, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT last_cursor FROM mes_cursor WHERE source_system = $1", conn);
        cmd.Parameters.AddWithValue(sourceSystem);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task SaveAsync(string sourceSystem, string cursor, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO mes_cursor (source_system, last_cursor, updated_at) VALUES ($1, $2, now())
            ON CONFLICT (source_system) DO UPDATE SET last_cursor = $2, updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(sourceSystem);
        cmd.Parameters.AddWithValue(cursor);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
