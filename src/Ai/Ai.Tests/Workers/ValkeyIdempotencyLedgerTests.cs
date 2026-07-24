using Ai.Domain.Jobs;
using Ai.Worker.Runtime;
using Xunit;

namespace Ai.Tests.Workers;

public class ValkeyIdempotencyLedgerTests
{
    private static readonly Guid JobId = Guid.NewGuid();

    [Fact]
    public async Task Degrada_para_em_memoria_quando_valkey_e_nulo()
    {
        var ledger = new ValkeyIdempotencyLedger(valkey: null);

        var claimResult = await ledger.TryClaimAsync(JobId);
        Assert.Equal(JobClaim.Accepted, claimResult);

        var secondClaim = await ledger.TryClaimAsync(JobId);
        Assert.Equal(JobClaim.InFlight, secondClaim);

        await ledger.CompleteAsync(JobId);

        var completedClaim = await ledger.TryClaimAsync(JobId);
        Assert.Equal(JobClaim.AlreadyDone, completedClaim);
    }

    [Fact]
    public async Task Release_devolve_job_ao_estado_aceito_no_fallback()
    {
        var ledger = new ValkeyIdempotencyLedger(valkey: null);

        await ledger.TryClaimAsync(JobId);
        await ledger.ReleaseAsync(JobId);

        var reClaim = await ledger.TryClaimAsync(JobId);
        Assert.Equal(JobClaim.Accepted, reClaim);
    }
}
