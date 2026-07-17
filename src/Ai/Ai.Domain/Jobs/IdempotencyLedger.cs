namespace Ai.Domain.Jobs;

public enum JobClaim
{
    Accepted,      // primeira vez: processe
    InFlight,      // outro worker está nele agora — não duplique
    AlreadyDone,   // resultado já existe — reprocesso NUNCA duplica resultado
}

/// <summary>
/// Idempotência por job-id: o Kafka entrega at-least-once, então o worker precisa
/// deduplicar. Semântica pura aqui; o estado real vai pro Valkey (SET NX + TTL)
/// na fase 1 pra valer entre réplicas.
/// A memória é limitada: só os últimos <c>maxCompletedRetained</c> concluídos ficam
/// retidos (janela FIFO) — mesmo trade-off do TTL no Valkey. Redelivery do Kafka é
/// questão de segundos/minutos; um job mais velho que a janela já foi commitado há muito.
/// </summary>
public sealed class IdempotencyLedger(int maxCompletedRetained = 100_000)
{
    private enum State { InFlight, Done }
    private readonly Dictionary<Guid, State> _jobs = [];
    private readonly Queue<Guid> _completedOrder = new();

    public JobClaim TryClaim(Guid jobId) => _jobs.TryGetValue(jobId, out var state)
        ? state == State.Done ? JobClaim.AlreadyDone : JobClaim.InFlight
        : Claim(jobId);

    public void Complete(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var state) && state == State.Done)
            return;

        _jobs[jobId] = State.Done;
        _completedOrder.Enqueue(jobId);

        while (_completedOrder.Count > maxCompletedRetained)
        {
            var oldest = _completedOrder.Dequeue();
            if (_jobs.TryGetValue(oldest, out var s) && s == State.Done)
                _jobs.Remove(oldest);
        }
    }

    /// <summary>Worker morreu no meio: libera o job pra outro tentar.</summary>
    public void Release(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var state) && state == State.InFlight)
            _jobs.Remove(jobId);
    }

    private JobClaim Claim(Guid jobId)
    {
        _jobs[jobId] = State.InFlight;
        return JobClaim.Accepted;
    }
}
