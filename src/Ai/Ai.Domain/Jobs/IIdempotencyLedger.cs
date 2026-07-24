namespace Ai.Domain.Jobs;

/// <summary>
/// Contrato de idempotência por job-id: permite reivindicação (claim), conclusão (complete)
/// e liberação (release) de jobs entre workers de IA, com suporte a implementações
/// distribuídas (ex: Valkey/Redis) e em memória.
/// </summary>
public interface IIdempotencyLedger
{
    Task<JobClaim> TryClaimAsync(Guid jobId, CancellationToken ct = default);
    Task CompleteAsync(Guid jobId, CancellationToken ct = default);
    Task ReleaseAsync(Guid jobId, CancellationToken ct = default);
}
