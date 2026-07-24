using Knowledge.Domain.Ishikawa;
using Knowledge.Domain.Rules;
using Xunit;

namespace Knowledge.Tests;

public class IshikawaRuleEngineTests
{
    [Fact]
    public void IshikawaRuleEngine_avalia_fatos_e_dispara_regras_satisfeitas()
    {
        var fatos = new[] { "TOMBSTONE", "Reflow_Gradiente_Elevado" };
        var resultados = IshikawaRuleEngine.Avaliar(fatos, IshikawaRuleEngine.RegrasPadrao);

        var diagnostico = Assert.Single(resultados);
        Assert.Equal("R-SMT-002", diagnostico.RuleId);
        Assert.Equal(IshikawaCategory.Maquina, diagnostico.Categoria);
        Assert.Equal(0.92, diagnostico.ConfiancaFinal);
        Assert.Equal(2, diagnostico.FatosSatisfeitos.Count);
        Assert.Contains("Racional", diagnostico.RacionalExplicativo, StringComparison.Ordinal);
    }

    [Fact]
    public void IshikawaRuleEngine_nao_dispara_se_faltar_condicao()
    {
        // Fato isolado "TOMBSTONE" sem "Reflow_Gradiente_Elevado" não satisfaz a regra R-SMT-002
        var fatos = new[] { "TOMBSTONE" };
        var resultados = IshikawaRuleEngine.Avaliar(fatos, IshikawaRuleEngine.RegrasPadrao);

        Assert.Empty(resultados);
    }

    [Fact]
    public void IshikawaRuleEngine_ordena_resultados_por_confianca_decrescente()
    {
        var fatos = new[] { "SPI_Volume_Baixo", "SOLDER-BRIDGE-RISK", "TOMBSTONE", "Reflow_Gradiente_Elevado" };
        var resultados = IshikawaRuleEngine.Avaliar(fatos, IshikawaRuleEngine.RegrasPadrao);

        Assert.Equal(2, resultados.Count);
        Assert.True(resultados[0].ConfiancaFinal >= resultados[1].ConfiancaFinal);
        Assert.Equal("R-SMT-002", resultados[0].RuleId); // 0.92 > 0.88
        Assert.Equal("R-SMT-001", resultados[1].RuleId);
    }

    [Fact]
    public void IshikawaRuleEngine_trata_fatos_com_espacos_e_case_insensitive()
    {
        var fatos = new[] { " spi_volume_baixo ", "SOLDER-BRIDGE-RISK" };
        var resultados = IshikawaRuleEngine.Avaliar(fatos, IshikawaRuleEngine.RegrasPadrao);

        var diagnostico = Assert.Single(resultados);
        Assert.Equal("R-SMT-001", diagnostico.RuleId);
    }
}
