using Knowledge.Domain.Ishikawa;

namespace Knowledge.Domain.Rules;

/// <summary>
/// Regra de inferência Ishikawa declarativa (Épico #10 - proposta Jeymerson).
/// Permite avaliar condições lógicas sobre fatos do processo (SPI, AOI, Reflow, PnP)
/// e derivar causas raízes categorizadas nas 6M com explicação auditável (XAI).
/// </summary>
public sealed record IshikawaRule(
    string RuleId,
    string Nome,
    IReadOnlyList<string> CondicoesFatos,
    IshikawaCategory Categoria,
    string CausaRaiz,
    string AcaoRecomendada,
    double Confianca,
    string Racional);

/// <summary>
/// Resultado da inferência dedutiva com a cadeia completa de explicação (XAI).
/// </summary>
public sealed record DiagnosticoIshikawa(
    string RuleId,
    IshikawaCategory Categoria,
    string CausaRaiz,
    string AcaoRecomendada,
    double ConfiancaFinal,
    IReadOnlyList<string> FatosSatisfeitos,
    string RacionalExplicativo);
