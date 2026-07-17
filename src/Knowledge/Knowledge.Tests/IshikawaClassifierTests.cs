using Knowledge.Domain.Ishikawa;
using Xunit;

namespace Knowledge.Tests;

public class IshikawaClassifierTests
{
    [Theory]
    [InlineData("Desgaste de estêncil na impressora", IshikawaCategory.Maquina)]
    [InlineData("SOLDER-BRIDGE: excesso de pasta de solda", IshikawaCategory.Material)]
    [InlineData("Operador do turno da noite sem treinamento", IshikawaCategory.MaoDeObra)]
    [InlineData("SPI descalibrado", IshikawaCategory.Medicao)]
    [InlineData("Umidade acima do limite na sala", IshikawaCategory.MeioAmbiente)]
    [InlineData("Setup errado na receita da linha", IshikawaCategory.Metodo)]
    public void Classifica_sintoma_na_categoria_6m(string sintoma, IshikawaCategory esperada)
    {
        Assert.Equal(esperada, IshikawaClassifier.Classify(sintoma));
    }

    [Theory]
    [InlineData("Erro de medição fora do padrão", IshikawaCategory.Medicao)] // regra é "medicao"
    [InlineData("CALIBRAÇÃO vencida no AOI-2", IshikawaCategory.Medicao)]
    [InlineData("Estêncil obstruído", IshikawaCategory.Maquina)] // regra é "estencil"
    public void Acentos_nao_mudam_a_classificacao(string sintoma, IshikawaCategory esperada)
    {
        Assert.Equal(esperada, IshikawaClassifier.Classify(sintoma));
    }

    [Fact]
    public void Sem_palavra_conhecida_e_indefinida()
    {
        Assert.Equal(IshikawaCategory.Indefinida, IshikawaClassifier.Classify("evento genérico xyz"));
    }

    [Fact]
    public void Classificacao_ignora_a_caixa()
    {
        Assert.Equal(IshikawaCategory.Maquina, IshikawaClassifier.Classify("ESTÊNCIL GASTO"));
    }

    [Fact]
    public void Primeira_regra_que_casa_vence()
    {
        var regras = new List<(string, IshikawaCategory)>
        {
            ("solda", IshikawaCategory.Material),
            ("estencil", IshikawaCategory.Maquina),
        };

        // contém as duas palavras — a ordem da lista decide.
        Assert.Equal(IshikawaCategory.Material, IshikawaClassifier.Classify("solda no estencil", regras));
    }
}
