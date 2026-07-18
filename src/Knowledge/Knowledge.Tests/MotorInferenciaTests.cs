using Knowledge.Domain.Inferencia;
using Xunit;

namespace Knowledge.Tests;

public class MotorInferenciaTests
{
    [Fact]
    public void Cadeia_de_duas_regras_deriva_contramedida_com_confianca_composta()
    {
        var conclusoes = MotorInferencia.Encadear(["SOLDER-BRIDGE"], MotorInferencia.RegrasSemente);

        var contramedida = conclusoes.Single(c => c.Fato.StartsWith("trocar/limpar", StringComparison.Ordinal));
        Assert.Equal(0.7 * 0.9, contramedida.Confianca, precision: 10); // produto: cadeia longa confia menos
        Assert.Equal(3, contramedida.Cadeia.Count); // observado → causa → contramedida (XAI completa)
        Assert.StartsWith("observado:", contramedida.Cadeia[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Fato_fora_das_regras_nao_deriva_nada()
    {
        Assert.Empty(MotorInferencia.Encadear(["XYZ-999"], MotorInferencia.RegrasSemente));
    }

    [Fact]
    public void Caminhos_concorrentes_ficam_com_a_maior_confianca()
    {
        var regras = new List<Regra>
        {
            new("sintoma", "causa", 0.5, "caminho fraco"),
            new("sintoma", "meio", 0.9, "via intermediária"),
            new("meio", "causa", 0.9, "caminho forte em dois passos"),
        };

        var causa = MotorInferencia.Encadear(["sintoma"], regras).Single(c => c.Fato == "causa");

        Assert.Equal(0.81, causa.Confianca, precision: 10); // 0.9×0.9 vence 0.5 direto
        Assert.Contains("caminho forte", causa.Cadeia[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Ciclo_nao_trava_o_motor()
    {
        var regras = new List<Regra>
        {
            new("a", "b", 0.9, "ida"),
            new("b", "a", 0.9, "volta"),
        };

        var conclusoes = MotorInferencia.Encadear(["a"], regras);

        Assert.Contains(conclusoes, c => c.Fato == "b"); // derivou e PAROU
        Assert.All(conclusoes, c => Assert.True(c.Confianca <= 0.9));
    }

    [Fact]
    public void Multiplos_sintomas_observados_derivam_em_paralelo()
    {
        var conclusoes = MotorInferencia.Encadear(
            ["SOLDER-BRIDGE", "TOMBSTONE"], MotorInferencia.RegrasSemente);

        Assert.Contains(conclusoes, c => c.Fato.Contains("estêncil", StringComparison.Ordinal));
        Assert.Contains(conclusoes, c => c.Fato.Contains("reperfilar", StringComparison.Ordinal));
    }
}
