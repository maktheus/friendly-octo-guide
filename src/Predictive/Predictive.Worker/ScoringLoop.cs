using System.Text.Json;
using Confluent.Kafka;
using Platform.Contracts;
using Platform.ServiceDefaults;
using Predictive.Domain.Scoring;

namespace Predictive.Worker;

public sealed partial class ScoringLoop(
    ServiceInstrumentation instrumentation,
    IConfiguration config,
    ILogger<ScoringLoop> log) : BackgroundService
{
    private const int MaxDetectors = 10_000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, AnomalyDetector> _detectors = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrap = config["Kafka:Bootstrap"] ?? "localhost:9092";
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "predictive",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap,
            EnableIdempotence = true,
        }).Build();

        consumer.Subscribe(config["Kafka:TelemetryTopic"] ?? "linha.telemetria.v1");
        var anomalies = instrumentation.Meter.CreateCounter<long>("predictive.anomalies");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            using var activity = instrumentation.Activity.StartActivity("predictive.score");

            SensorReadingRecord reading;
            try
            {
                reading = SensorReadingCodec.Decode(result.Message.Value);
            }
            catch (FormatException)
            {
                consumer.Commit(result); // quarentena é papel do ingest; aqui só não trava o scoring
                continue;
            }

            if (!_detectors.TryGetValue(reading.SensorId, out var detector))
            {
                // sensor_id é entrada externa: um edge malcomportado (id único por mensagem)
                // cresceria o dicionário até OOM. Melhor re-aquecer 30 amostras do que cair.
                if (_detectors.Count >= MaxDetectors)
                {
                    LogDetectorReset(_detectors.Count);
                    _detectors.Clear();
                }

                detector = _detectors[reading.SensorId] = new AnomalyDetector();
            }

            var score = detector.Observe(reading.Value);
            activity?.SetTag("sensor.id", reading.SensorId);
            activity?.SetTag("anomaly.z", score.ZScore);

            if (score.IsAnomaly)
            {
                anomalies.Add(1, new KeyValuePair<string, object?>("sensor", reading.SensorId));
                LogAnomaly(reading.SensorId, reading.Value, score.ZScore);

                // Mesmo envelope que o Notifications consome — anomalia VIRA alerta.
                var alert = JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid(),
                    title = $"Anomalia em {reading.SensorId}",
                    body = $"Valor {reading.Value:F2} está a {Math.Abs(score.ZScore):F1} desvios do regime.",
                    severity = "Warning",
                    raisedAt = DateTimeOffset.UtcNow,
                }, JsonOpts);

                await producer.ProduceAsync(config["Kafka:AlertsTopic"] ?? "linha.alertas.v1",
                    new Message<string, string> { Key = reading.SensorId, Value = alert }, stoppingToken);
            }

            consumer.Commit(result);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anomalia: {Sensor} = {Value} (z = {ZScore:F1})")]
    private partial void LogAnomaly(string sensor, double value, double zScore);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Teto de {Count} detectores atingido — baselines descartados; sensor_id malformado no edge?")]
    private partial void LogDetectorReset(int count);
}
