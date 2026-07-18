using Agents.Domain.Diagnosis;
using Predictive.Domain.Diagnosis;

namespace Agents.Domain.Idmss;

/// <summary>Diagnóstico do iDMSS pra um ativo: quantos eventos MES na janela e o ranking explicável.</summary>
public sealed record IdmssResult(string AtivoId, int Ocorrencias, IReadOnlyList<RankedCause> Ranking);

/// <summary>
/// Interface do iDMSS (épico #8, proposta Victor): a janela de eventos MES do ativo
/// vira evidência por sintoma e o ranking explicável vem da camada de processamento
/// (<see cref="DiagnosisRanking"/>, Predictive.Domain — referência pura, sem IO).
/// O peso do modelo é plugável: 1.0 é o baseline por frequência; o Random Forest
/// servido (épico #11) entra por <paramref name="pesoModelo"/> sem mudar nada aqui.
/// </summary>
public static class IdmssDiagnosis
{
    public static IdmssResult Rank(
        IReadOnlyList<Signal> janela, string ativoId, int top,
        Func<string, double>? pesoModelo = null)
    {
        ArgumentNullException.ThrowIfNull(janela);
        ArgumentException.ThrowIfNullOrWhiteSpace(ativoId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);

        var doAtivo = janela
            .Where(s => s.Kind == SignalKind.MesEvento
                     && string.Equals(s.Resource, ativoId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var evidencias = doAtivo
            .GroupBy(s => s.Message, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SymptomEvidence(g.Key, g.Count(), pesoModelo?.Invoke(g.Key) ?? 1.0));

        return new IdmssResult(ativoId, doAtivo.Count, DiagnosisRanking.Rank(evidencias, top));
    }
}
