using Knowledge.Domain.Ishikawa;
using Xunit;

namespace Knowledge.Tests;

public class DefeitoSmtTests
{
    [Theory]
    [InlineData("SOLDER-BRIDGE", IshikawaCategory.Maquina)]
    [InlineData("solder-bridge", IshikawaCategory.Maquina)] // caixa não importa
    [InlineData("INSUFFICIENT-PASTE", IshikawaCategory.Material)]
    [InlineData("EXCESS-PASTE", IshikawaCategory.Metodo)]
    [InlineData("TOMBSTONE", IshikawaCategory.Metodo)]
    [InlineData("MISSING-COMPONENT", IshikawaCategory.Material)]
    public void Defeito_curado_mapeia_na_categoria_com_racional(string defeito, IshikawaCategory esperada)
    {
        var (categoria, racional) = DefeitoSmt.Map(defeito);
        Assert.Equal(esperada, categoria);
        Assert.False(string.IsNullOrWhiteSpace(racional)); // XAI: sempre com o porquê
    }

    [Fact]
    public void Fora_do_mapa_curado_cai_no_classificador_por_palavra()
    {
        var (categoria, racional) = DefeitoSmt.Map("DESGASTE-ESTENCIL-BORDA");
        Assert.Equal(IshikawaCategory.Maquina, categoria);
        Assert.Contains("palavra-chave", racional, StringComparison.Ordinal);
    }

    [Fact]
    public void Desconhecido_e_indefinida_pedindo_elicitacao_nunca_inventa()
    {
        var (categoria, racional) = DefeitoSmt.Map("XYZ-999");
        Assert.Equal(IshikawaCategory.Indefinida, categoria);
        Assert.Contains("elicitar", racional, StringComparison.Ordinal);
    }
}
