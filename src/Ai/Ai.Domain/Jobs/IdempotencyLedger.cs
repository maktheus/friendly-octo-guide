namespace Ai.Domain.Jobs;

public enum JobClaim
{
    Accepted,      // primeira vez: processe
    InFlight,      // outro worker está nele agora — não duplique
    AlreadyDone,   // resultado já existe — reprocesso NUNCA duplica resultado
}

/// <summary>
/// Idempotência por job-id em memória: o Kafka entrega at-least-once, então o worker precisa
/// deduplicar. Semântica pura aqui; o estado real distribuído vai pro Valkey (SET NX + TTL)
/// no ValkeyIdempotencyLedger pra valer entre réplicas.
/// A memória é limitada: só os últimos <c>maxCompletedRetained</c> concluídos ficam
/// retidos (janela FIFO) — mesmo trade-off do TTL no Valkey.
/// </summary>
public class IdempotencyLedger : IIdempotencyLedger
{
    private enum State { InFlight, Done }
    private readonly Dictionary<Guid, State> _jobs = [];
    private readonly Queue<Guid> _completedOrder = new();
    private readonly int _maxCompletedRetained;

    public IdempotencyLedger(int maxCompletedRetained = 100_000)
    {
        _maxCompletedRetained = maxCompletedRetained;
    }

    public JobClaim TryClaim(Guid jobId) => _jobs.TryGetValue(jobId, out var state)
        ? state == State.Done ? JobClaim.AlreadyDone : JobClaim.InFlight
        : Claim(jobId);

    public Task<JobClaim> TryClaimAsync(Guid jobId, CancellationToken ct = default) =>
        Task.FromResult(TryClaim(jobId));

    public void Complete(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var state) && state == State.Done)
            return;

        _jobs[jobId] = State.Done;
        _completedOrder.Enqueue(jobId);

        while (_completedOrder.Count > _maxCompletedRetained)
        {
            var oldest = _completedOrder.Dequeue();
            if (_jobs.TryGetValue(oldest, out var s) && s == State.Done)
                _jobs.Remove(oldest);
        }
    }

    public Task CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        Complete(jobId);
        return Task.CompletedTask;
    }

    /// <summary>Worker morreu no meio: libera o job pra outro tentar.</summary>
    public void Release(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var state) && state == State.InFlight)
            _jobs.Remove(jobId);
    }

    public Task ReleaseAsync(Guid jobId, CancellationToken ct = default)
    {
        Release(jobId);
        return Task.CompletedTask;
    }

    private JobClaim Claim(Guid jobId)
    {
        _jobs[jobId] = State.InFlight;
        return JobClaim.Accepted;
    }
}

/// <summary>Alias explícito para a implementação em memória de <see cref="IIdempotencyLedger"/>.</summary>
public sealed class InMemoryIdempotencyLedger(int maxCompletedRetained = 100_000)
    : IdempotencyLedger(maxCompletedRetained);
