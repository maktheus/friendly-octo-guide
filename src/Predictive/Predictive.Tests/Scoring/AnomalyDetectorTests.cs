using Predictive.Domain.Scoring;
using Xunit;

namespace Predictive.Tests.Scoring;

public class AnomalyDetectorTests
{
    [Fact]
    public void Nao_alarma_durante_o_warmup()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        // Mesmo um salto brutal nas primeiras leituras não pode acordar ninguém.
        for (var i = 0; i < 10; i++)
            detector.Observe(100);
        var score = detector.Observe(10_000);
        Assert.False(score.IsAnomaly);
        Assert.False(score.WarmedUp);
    }

    [Fact]
    public void Regime_estavel_nao_gera_anomalia()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        var rng = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            var score = detector.Observe(100 + rng.NextDouble() * 2 - 1); // ruído ±1
            if (i > 30)
                Assert.False(score.IsAnomaly);
        }
    }

    [Fact]
    public void Salto_fora_do_regime_e_anomalia_apos_warmup()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        var rng = new Random(42);
        for (var i = 0; i < 100; i++)
            detector.Observe(100 + rng.NextDouble() * 2 - 1);

        var score = detector.Observe(150); // 50 unidades acima de um regime com desvio ~0.6
        Assert.True(score.IsAnomaly);
        Assert.True(Math.Abs(score.ZScore) > 3);
    }

    [Fact]
    public void Sensor_flatline_que_salta_e_anomalia_apos_warmup()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        for (var i = 0; i < 60; i++)
            detector.Observe(100); // variância zero: linha parada ou setpoint fixo

        var score = detector.Observe(140);
        Assert.True(score.IsAnomaly); // desvio com variância zero é quebra de regime
    }

    [Fact]
    public void Sensor_flatline_estavel_nao_gera_anomalia()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        AnomalyScore score = default!;
        for (var i = 0; i < 60; i++)
            score = detector.Observe(100);

        Assert.False(score.IsAnomaly);
        Assert.Equal(0, score.ZScore);
    }

    [Fact]
    public void Anomalia_nao_contamina_a_linha_de_base()
    {
        var detector = new AnomalyDetector(warmupSamples: 30);
        var rng = new Random(42);
        for (var i = 0; i < 100; i++)
            detector.Observe(100 + rng.NextDouble() * 2 - 1);

        detector.Observe(150);                 // pico anômalo
        var next = detector.Observe(150);      // pico persiste
        Assert.True(next.IsAnomaly);           // continua anômalo: baseline não "aprendeu" o defeito
    }
}

public class DriftMonitorTests
{
    [Fact]
    public void Modelo_acurado_nao_deriva()
    {
        var monitor = new DriftMonitor(maeThreshold: 1.0);
        DriftReading last = default!;
        for (var i = 0; i < 50; i++)
            last = monitor.Compare(predicted: 100, actual: 100.2);
        Assert.False(last.Drifting);
    }

    [Fact]
    public void Erro_sistematico_acima_do_limiar_e_drift()
    {
        var monitor = new DriftMonitor(maeThreshold: 1.0);
        DriftReading last = default!;
        for (var i = 0; i < 50; i++)
            last = monitor.Compare(predicted: 100, actual: 105);
        Assert.True(last.Drifting);
        Assert.True(last.MeanAbsoluteError > 4);
    }

    [Fact]
    public void Poucas_amostras_nao_declaram_drift()
    {
        var monitor = new DriftMonitor(maeThreshold: 1.0);
        var reading = monitor.Compare(predicted: 100, actual: 200);
        Assert.False(reading.Drifting); // 1 erro grande ≠ drift; precisa de evidência
    }
}
