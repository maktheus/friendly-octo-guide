using Platform.Contracts;
using Xunit;

namespace Contracts.Tests;

public class InspecaoSmtCodecTests
{
    private static InspecaoSmtRecord Inspecao(string? modelo = null, string? explicacao = null, double confianca = 1.0) => new(
        InspecaoId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AtivoId: "envase.spitau.linha2.spi",
        Estagio: EstagioInspecao.Spi,
        Veredito: VereditoInspecao.Suspeito,
        TipoDefeito: "SOLDER-BRIDGE",
        ImageRef: "s3://linha-lake/inspecao/dt=2026-07-18/placa-4471.png",
        Parametros: """{"alturaUm":152.3,"areaPct":94.1}""",
        Modelo: modelo,
        Confianca: confianca,
        Explicacao: explicacao,
        OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));

    [Fact]
    public void Ida_e_volta_preserva_o_registro()
    {
        var original = Inspecao(modelo: "vit-spi@1.2", explicacao: "região do pad 14 com continuidade de pasta", confianca: 0.87);
        var decoded = InspecaoSmtCodec.Decode(InspecaoSmtCodec.Encode(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Imagem_vai_por_referencia_nunca_inline()
    {
        var json = InspecaoSmtCodec.Encode(Inspecao());
        Assert.Contains("s3://linha-lake", json, StringComparison.Ordinal); // ponteiro
        Assert.DoesNotContain("base64", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inferencia_sem_explicacao_e_payload_invalido()
    {
        // XAI é contrato: modelo preenchido exige o porquê.
        Assert.Throws<FormatException>(() => InspecaoSmtCodec.Encode(Inspecao(modelo: "vit-spi@1.2", explicacao: null)));
    }

    [Fact]
    public void Confianca_fora_da_faixa_e_payload_invalido()
    {
        Assert.Throws<FormatException>(() => InspecaoSmtCodec.Encode(Inspecao(confianca: 1.7)));
    }

    [Fact]
    public void Veredito_da_maquina_dispensa_modelo_e_explicacao()
    {
        var decoded = InspecaoSmtCodec.Decode(InspecaoSmtCodec.Encode(Inspecao()));
        Assert.Null(decoded.Modelo);
        Assert.Equal(1.0, decoded.Confianca);
    }
}
