using System.Data.Common;
using Mes.Connector.Domain;

namespace Mes.Connector.Worker;

/// <summary>
/// Loop de coleta: poll do adapter → só o que é novo (cursor) → normaliza → Kafka.
/// Invariantes:
///   1. Nunca perde: linha que não normaliza vai pra quarentena com o motivo.
///   2. Nunca reprocessa: o cursor (idempotência) filtra o que já passou — e é
///      DURÁVEL com Postgres configurado (restart retoma de onde parou).
///   3. MES fora do ar não derruba o worker: loga e tenta no próximo poll, sem
///      nunca avançar cursor de leitura que não aconteceu.
/// </summary>
public sealed partial class MesConnectorWorker(
    MesOptions options,
    IMesAdapter adapter,
    MesEventSink sink,
    ICursorStore cursors,
    ILogger<MesConnectorWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStart(options.EventTopic, options.SourceSystem);
        var persisted = await cursors.LoadAsync(options.SourceSystem, stoppingToken);
        var state = persisted is null ? MesPollState.Start : new MesPollState(persisted);
        if (persisted is not null)
            LogResumed(persisted);

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<RawMesRow> rows;
            try
            {
                rows = await adapter.PollAsync(state.LastCursor, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException or DbException)
            {
                LogPollFailed(e);
                await Task.Delay(options.PollInterval, stoppingToken);
                continue;
            }

            var (fresh, next) = PollCursor.SelectNew(rows, state);

            foreach (var row in fresh)
            {
                using var activity = MesTelemetry.Activity.StartActivity("mes.event");
                var result = MesNormalizer.Normalize(row, options.SourceSystem, Guid.NewGuid);

                if (result.Accepted && result.Event is { } evento)
                {
                    activity?.SetTag("ativo.id", evento.AtivoId);
                    await sink.PublishAsync(evento, stoppingToken);
                    MesTelemetry.Published.Add(1);
                }
                else
                {
                    await sink.QuarantineAsync(row, result.Reason, stoppingToken);
                    MesTelemetry.Quarantined.Add(1, new KeyValuePair<string, object?>("reason", result.Reason));
                    LogQuarantined(row.Cursor, result.Reason);
                }
            }

            // Persiste ANTES de trocar o estado local: se cair aqui, o pior caso é
            // re-pollar um lote (consumidor deduplica por event_id) — nunca pular um.
            if (next.LastCursor is { } advanced && advanced != state.LastCursor)
                await cursors.SaveAsync(options.SourceSystem, advanced, stoppingToken);

            state = next;
            await Task.Delay(options.PollInterval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Conector MES iniciado → {Topic} (origem {Source})")]
    private partial void LogStart(string topic, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cursor retomado do store durável: {Cursor}")]
    private partial void LogResumed(string cursor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poll do MES falhou — tentando de novo no próximo intervalo")]
    private partial void LogPollFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Linha MES em quarentena: {Cursor} · {Reason}")]
    private partial void LogQuarantined(string cursor, string reason);
}
