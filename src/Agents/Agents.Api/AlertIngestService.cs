using System.Text.Json;
using Agents.Domain.Diagnosis;
using Confluent.Kafka;

namespace Agents.Api;

/// <summary>
/// Alimenta a janela de correlação do agente: consome linha.alertas.v1 e converte
/// cada alerta num Signal. É a fonte quente; a correlação histórica além da janela
/// sai do TSDB/Big Data Pool (fase 1).
/// </summary>
public sealed class AlertIngestService(SignalWindow window, IConfiguration config) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        // Kafka.Consume bloqueia; roda o loop fora do thread de start do host.
        => Task.Run(() => Consume(stoppingToken), stoppingToken);

    private void Consume(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:Bootstrap"] ?? "localhost:9092",
            GroupId = "agents",
            AutoOffsetReset = AutoOffsetReset.Latest, // o agente olha o presente, não replay
        }).Build();
        consumer.Subscribe(config["Kafka:AlertsTopic"] ?? "linha.alertas.v1");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            AlertEnvelope? alert;
            try
            {
                alert = JsonSerializer.Deserialize<AlertEnvelope>(result.Message.Value, JsonOpts);
            }
            catch (JsonException)
            {
                continue; // payload ruim não derruba a janela de correlação
            }

            if (alert is null)
                continue;

            window.Add(new Signal(
                SignalKind.Alert,
                Resource: result.Message.Key ?? alert.SensorId ?? "desconhecido",
                Severity: ParseSeverity(alert.Severity),
                At: alert.RaisedAt ?? DateTimeOffset.UtcNow,
                Message: alert.Title ?? alert.Body ?? "",
                TraceId: alert.TraceId), DateTimeOffset.UtcNow);
        }
    }

    private static Severity ParseSeverity(string? s) => s?.ToLowerInvariant() switch
    {
        "critical" or "critico" => Severity.Critical,
        "error" or "erro" => Severity.Error,
        "warning" or "aviso" => Severity.Warning,
        _ => Severity.Info,
    };

    private sealed record AlertEnvelope(
        string? SensorId, string? Title, string? Body, string? Severity,
        DateTimeOffset? RaisedAt, string? TraceId);
}
