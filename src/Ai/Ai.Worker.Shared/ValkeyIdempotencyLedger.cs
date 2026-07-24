using System.Diagnostics.Metrics;
using Ai.Domain.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Ai.Worker.Runtime;

/// <summary>
/// Idempotência distribuída no Valkey (ou Redis) para os workers de IA.
/// Utiliza operações atômicas <c>SET NX</c> com TTL para garantir que réplicas concorrentes no
/// cluster Kubernetes não processem o mesmo <c>job_id</c> em duplicidade.
/// Se o Valkey estiver indisponível, degrada com segurança para o limitador em memória
/// (<see cref="InMemoryIdempotencyLedger"/>) emitindo métrica de degradação.
/// </summary>
public sealed partial class ValkeyIdempotencyLedger : IIdempotencyLedger
{
    private static readonly TimeSpan DefaultInFlightTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultDoneTtl = TimeSpan.FromDays(1);

    private readonly IConnectionMultiplexer? _valkey;
    private readonly InMemoryIdempotencyLedger _fallback;
    private readonly ILogger<ValkeyIdempotencyLedger> _log;
    private readonly Counter<long>? _degradedCounter;
    private readonly TimeSpan _inFlightTtl;
    private readonly TimeSpan _doneTtl;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey indisponível pro job {JobId}; degradado pro ledger em memória")]
    private static partial void LogDegraded(ILogger logger, Guid jobId, Exception ex);

    public ValkeyIdempotencyLedger(
        IConnectionMultiplexer? valkey,
        ILogger<ValkeyIdempotencyLedger>? log = null,
        Meter? meter = null,
        TimeSpan? inFlightTtl = null,
        TimeSpan? doneTtl = null)
    {
        _valkey = valkey;
        _log = log ?? NullLogger<ValkeyIdempotencyLedger>.Instance;
        _fallback = new InMemoryIdempotencyLedger();
        _degradedCounter = meter?.CreateCounter<long>("ai.idempotency.degraded");
        _inFlightTtl = inFlightTtl ?? DefaultInFlightTtl;
        _doneTtl = doneTtl ?? DefaultDoneTtl;
    }

    public async Task<JobClaim> TryClaimAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_valkey is null || !_valkey.IsConnected)
        {
            RecordDegraded(jobId, new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Valkey não conectado"));
            return await _fallback.TryClaimAsync(jobId, ct);
        }

        try
        {
            var db = _valkey.GetDatabase();
            var key = GetKey(jobId);

            // SET key "IN_FLIGHT" NX EX inFlightTtl — atômico entre réplicas
            var claimed = await db.StringSetAsync(key, "IN_FLIGHT", _inFlightTtl, When.NotExists);
            if (claimed)
            {
                return JobClaim.Accepted;
            }

            var existingValue = await db.StringGetAsync(key);
            if (existingValue == "DONE")
            {
                return JobClaim.AlreadyDone;
            }

            return JobClaim.InFlight;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            RecordDegraded(jobId, ex);
            return await _fallback.TryClaimAsync(jobId, ct);
        }
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_valkey is null || !_valkey.IsConnected)
        {
            await _fallback.CompleteAsync(jobId, ct);
            return;
        }

        try
        {
            var db = _valkey.GetDatabase();
            var key = GetKey(jobId);
            await db.StringSetAsync(key, "DONE", _doneTtl);
            await _fallback.CompleteAsync(jobId, ct);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            RecordDegraded(jobId, ex);
            await _fallback.CompleteAsync(jobId, ct);
        }
    }

    public async Task ReleaseAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_valkey is null || !_valkey.IsConnected)
        {
            await _fallback.ReleaseAsync(jobId, ct);
            return;
        }

        try
        {
            var db = _valkey.GetDatabase();
            var key = GetKey(jobId);
            var val = await db.StringGetAsync(key);

            if (val == "IN_FLIGHT")
            {
                await db.KeyDeleteAsync(key);
            }

            await _fallback.ReleaseAsync(jobId, ct);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            RecordDegraded(jobId, ex);
            await _fallback.ReleaseAsync(jobId, ct);
        }
    }

    private static string GetKey(Guid jobId) => $"ai:job:{jobId}";

    private void RecordDegraded(Guid jobId, Exception ex)
    {
        _degradedCounter?.Add(1);
        LogDegraded(_log, jobId, ex);
    }
}
