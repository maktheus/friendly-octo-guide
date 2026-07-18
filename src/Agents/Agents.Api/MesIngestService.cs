using Agents.Domain.Diagnosis;
using Confluent.Kafka;
using Platform.Contracts;

namespace Agents.Api;

/// <summary>
/// Alimenta o iDMSS: consome mes.eventos.v1 (Avro) e converte cada evento MES num
/// Signal da janela de correlação — parada/defeito pesam como Warning, o resto é
/// contexto (Info). Mesma filosofia do AlertIngestService: o agente olha o presente
/// (Latest); histórico além da janela sai do lake (fase 1).
/// </summary>
public sealed class MesIngestService(SignalWindow window, IConfiguration config) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => Consume(stoppingToken), stoppingToken);

    private void Consume(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:Bootstrap"] ?? "localhost:9092",
            GroupId = "agents-mes",
            AutoOffsetReset = AutoOffsetReset.Latest,
        }).Build();
        consumer.Subscribe(config["Kafka:MesTopic"] ?? "mes.eventos.v1");

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string> result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException)
            {
                continue;
            }

            MesEventoRecord evento;
            try
            {
                evento = MesEventoCodec.Decode(result.Message.Value);
            }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
            {
                continue; // malformado já foi pra quarentena na origem
            }

            window.Add(new Signal(
                SignalKind.MesEvento,
                Resource: evento.AtivoId,
                Severity: evento.Tipo is TipoMesEvento.Parada or TipoMesEvento.Defeito
                    ? Severity.Warning
                    : Severity.Info,
                At: evento.OccurredAt,
                Message: evento.Texto ?? $"{evento.Tipo}: {evento.Codigo}"), DateTimeOffset.UtcNow);
        }
    }
}
