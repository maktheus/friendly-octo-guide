using Ai.Domain.Jobs;
using Xunit;

namespace Ai.Tests.Jobs;

public class DispatchPolicyTests
{
    private static AiJob Job(string modelType, int attempts = 0) =>
        new(Guid.NewGuid(), modelType, "{}", attempts);

    [Theory]
    [InlineData("llm", "ai.jobs.llm.v1")]
    [InlineData("vision", "ai.jobs.vision.v1")]
    [InlineData("embedding", "ai.jobs.embedding.v1")]
    [InlineData("LLM", "ai.jobs.llm.v1")] // tipo é indiferente a caixa
    public void Tipo_conhecido_encaminha_pro_topico_do_worker(string modelType, string expectedTopic)
    {
        var decision = DispatchPolicy.Decide(Job(modelType));
        Assert.Equal(RouteKind.Forward, decision.Kind);
        Assert.Equal(expectedTopic, decision.Topic);
    }

    [Fact]
    public void Tipo_desconhecido_vai_pra_DLQ_com_motivo()
    {
        var decision = DispatchPolicy.Decide(Job("clarividencia"));
        Assert.Equal(RouteKind.DeadLetter, decision.Kind);
        Assert.Equal(DispatchPolicy.DlqTopic, decision.Topic);
        Assert.Contains("clarividencia", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Tentativas_esgotadas_vao_pra_DLQ_mesmo_com_tipo_valido()
    {
        var decision = DispatchPolicy.Decide(Job("llm", attempts: DispatchPolicy.MaxAttempts));
        Assert.Equal(RouteKind.DeadLetter, decision.Kind);
    }

    [Fact]
    public void Ultima_tentativa_valida_ainda_encaminha()
    {
        var decision = DispatchPolicy.Decide(Job("llm", attempts: DispatchPolicy.MaxAttempts - 1));
        Assert.Equal(RouteKind.Forward, decision.Kind);
    }
}

public class IdempotencyLedgerTests
{
    private readonly IdempotencyLedger _ledger = new();
    private static readonly Guid JobId = Guid.NewGuid();

    [Fact]
    public void Primeira_reivindicacao_e_aceita()
    {
        Assert.Equal(JobClaim.Accepted, _ledger.TryClaim(JobId));
    }

    [Fact]
    public void Job_em_voo_nao_e_dado_a_outro_worker()
    {
        _ledger.TryClaim(JobId);
        Assert.Equal(JobClaim.InFlight, _ledger.TryClaim(JobId));
    }

    [Fact]
    public void Reprocesso_de_job_concluido_nunca_duplica_resultado()
    {
        _ledger.TryClaim(JobId);
        _ledger.Complete(JobId);
        Assert.Equal(JobClaim.AlreadyDone, _ledger.TryClaim(JobId));
    }

    [Fact]
    public void Release_devolve_job_em_voo_pra_fila()
    {
        _ledger.TryClaim(JobId);
        _ledger.Release(JobId);
        Assert.Equal(JobClaim.Accepted, _ledger.TryClaim(JobId));
    }

    [Fact]
    public void Release_nao_apaga_job_concluido()
    {
        _ledger.TryClaim(JobId);
        _ledger.Complete(JobId);
        _ledger.Release(JobId);
        Assert.Equal(JobClaim.AlreadyDone, _ledger.TryClaim(JobId));
    }

    [Fact]
    public void Concluidos_alem_da_janela_sao_esquecidos_e_a_memoria_nao_cresce_sem_limite()
    {
        var ledger = new IdempotencyLedger(maxCompletedRetained: 2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        foreach (var id in (Guid[])[first, second, third])
        {
            ledger.TryClaim(id);
            ledger.Complete(id);
        }

        Assert.Equal(JobClaim.AlreadyDone, ledger.TryClaim(second));  // dentro da janela
        Assert.Equal(JobClaim.AlreadyDone, ledger.TryClaim(third));
        Assert.Equal(JobClaim.Accepted, ledger.TryClaim(first));      // saiu da janela: reprocessável
    }

    [Fact]
    public void Complete_repetido_nao_infla_a_janela()
    {
        var ledger = new IdempotencyLedger(maxCompletedRetained: 2);
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();

        ledger.TryClaim(jobA);
        ledger.Complete(jobA);
        ledger.Complete(jobA); // idempotente: não conta duas vezes na janela
        ledger.TryClaim(jobB);
        ledger.Complete(jobB);

        Assert.Equal(JobClaim.AlreadyDone, ledger.TryClaim(jobA));
        Assert.Equal(JobClaim.AlreadyDone, ledger.TryClaim(jobB));
    }
}
