using Agents.Domain.Diagnosis;
using Agents.Domain.Idmss;
using Xunit;

namespace Agents.Tests;

public class IdmssDiagnosisTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Signal Mes(string ativo, string sintoma) =>
        new(SignalKind.MesEvento, ativo, Severity.Warning, T, sintoma);

    [Fact]
    public void Sintoma_mais_frequente_lidera_o_ranking_com_a_conta_visivel()
    {
        var janela = new[]
        {
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-2", "Defeito: SOLDER-BRIDGE"),
        };

        var result = IdmssDiagnosis.Rank(janela, "linha-2", top: 2);

        Assert.Equal(4, result.Ocorrencias);
        Assert.Equal("Parada: NOZZLE-CLOG", result.Ranking[0].Sintoma);
        Assert.Equal(3, result.Ranking[0].Score);              // 3 × peso 1.0 (baseline)
        Assert.Contains("3×", result.Ranking[0].Explicacao, StringComparison.Ordinal); // XAI: a conta vai junto
    }

    [Fact]
    public void So_eventos_MES_do_ativo_pedido_entram_na_conta()
    {
        var janela = new[]
        {
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-3", "Parada: NOZZLE-CLOG"),                       // outro ativo
            new Signal(SignalKind.Alert, "linha-2", Severity.Warning, T, // alerta não é evento MES
                "Anomalia em temp-forno-01"),
        };

        var result = IdmssDiagnosis.Rank(janela, "linha-2", top: 5);

        Assert.Equal(1, result.Ocorrencias);
        Assert.Single(result.Ranking);
    }

    [Fact]
    public void Peso_do_modelo_reordena_sem_mudar_a_evidencia()
    {
        // O RF servido (épico #11) pluga por aqui: frequência menor pode vencer se o
        // modelo der peso maior — e a explicação continua carregando a conta.
        var janela = new[]
        {
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-2", "Parada: NOZZLE-CLOG"),
            Mes("linha-2", "Defeito: SOLDER-BRIDGE"),
        };

        var result = IdmssDiagnosis.Rank(janela, "linha-2", top: 2,
            pesoModelo: s => s.Contains("SOLDER", StringComparison.Ordinal) ? 5.0 : 1.0);

        Assert.Equal("Defeito: SOLDER-BRIDGE", result.Ranking[0].Sintoma); // 1×5.0 > 2×1.0
    }

    [Fact]
    public void Janela_sem_evento_do_ativo_devolve_ranking_vazio()
    {
        var result = IdmssDiagnosis.Rank([], "linha-2", top: 3);
        Assert.Equal(0, result.Ocorrencias);
        Assert.Empty(result.Ranking);
    }
}
