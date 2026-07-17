namespace Predictive.Domain.Scoring;

public sealed record AnomalyScore(double ZScore, bool IsAnomaly, bool WarmedUp);

/// <summary>
/// Scoring online sobre o stream: média e variância exponencialmente ponderadas (EWMA),
/// anomalia = |z-score| acima do limiar. Incremental e O(1) por leitura — roda no fluxo,
/// não em batch. Warmup evita alarme falso enquanto o detector ainda não viu a linha
/// operar. É o degrau "o que vai acontecer?" (acatech 5): desvio vira alerta ANTES
/// do limite físico estourar.
/// </summary>
public sealed class AnomalyDetector(double alpha = 0.05, double zThreshold = 3.0, int warmupSamples = 30)
{
    private double _mean;
    private double _variance;
    private int _samples;

    public AnomalyScore Observe(double value)
    {
        _samples++;

        if (_samples == 1)
        {
            _mean = value;
            _variance = 0;
            return new AnomalyScore(0, false, false);
        }

        var deviation = value - _mean;
        var stdDev = Math.Sqrt(_variance);
        // Regime flatline (variância zero): qualquer desvio é quebra de regime — zerar o z
        // aqui faria o salto mais óbvio passar sem alarme e ser absorvido pela média.
        var z = stdDev > 0 ? deviation / stdDev
            : deviation == 0 ? 0
            : Math.Sign(deviation) * double.PositiveInfinity;
        var warmedUp = _samples > warmupSamples;

        // Anomalia NÃO contamina a linha de base: o normal não pode "aprender" o defeito.
        var isAnomaly = warmedUp && Math.Abs(z) > zThreshold;
        if (!isAnomaly)
        {
            _mean += alpha * deviation;
            _variance = (1 - alpha) * (_variance + alpha * deviation * deviation);
        }

        return new AnomalyScore(z, isAnomaly, warmedUp);
    }
}
