using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.Contracts;

/// <summary>Estágio da inspeção — espelho do enum de schemas/inspecao-smt.avsc.</summary>
public enum EstagioInspecao { Spi, Aoi }

/// <summary>Veredito — Suspeito exige revisão humana (o modelo nunca reprova sozinho placa limítrofe).</summary>
public enum VereditoInspecao { Aprovado, Reprovado, Suspeito }

/// <summary>Espelho de schemas/inspecao-smt.avsc — linha.inspecao.v1. DTO puro.</summary>
[ExcludeFromCodeCoverage]
public sealed record InspecaoSmtRecord(
    Guid InspecaoId,
    string AtivoId,
    EstagioInspecao Estagio,
    VereditoInspecao Veredito,
    string? TipoDefeito,
    string? ImageRef,
    string? Parametros,
    string? Modelo,
    double Confianca,
    string? Explicacao,
    DateTimeOffset OccurredAt,
    int ClockSource = 0);

/// <summary>
/// Codec da inspeção SMT (épico #9). JSON na fase 0, como o mes-evento (evento de
/// negócio, baixo volume — a IMAGEM não passa por aqui, só o ponteiro image_ref).
/// Invariante do contrato: inferência de modelo sem explicação (XAI) é payload
/// inválido — modelo preenchido exige explicacao.
/// </summary>
[ExcludeFromCodeCoverage]
public static class InspecaoSmtCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(InspecaoSmtRecord inspecao)
    {
        ArgumentNullException.ThrowIfNull(inspecao);
        Validate(inspecao);
        return JsonSerializer.Serialize(inspecao, Options);
    }

    public static InspecaoSmtRecord Decode(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var inspecao = JsonSerializer.Deserialize<InspecaoSmtRecord>(json, Options)
            ?? throw new FormatException("Payload de inspeção vazio ou inválido.");
        Validate(inspecao);
        return inspecao;
    }

    private static void Validate(InspecaoSmtRecord inspecao)
    {
        if (inspecao.Modelo is not null && string.IsNullOrWhiteSpace(inspecao.Explicacao))
            throw new FormatException(
                "Inferência de modelo sem explicação (XAI) não entra na linha — preencha explicacao.");
        if (inspecao.Confianca is < 0 or > 1)
            throw new FormatException("Confiança fora de [0,1].");
    }
}
