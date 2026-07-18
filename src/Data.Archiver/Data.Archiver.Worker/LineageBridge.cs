using Confluent.Kafka;

namespace Data.Archiver.Worker;

/// <summary>
/// Fecha o elo linhagem → Marquez: consome linhagem.openlineage.v1 e entrega cada
/// RunEvent na API HTTP do Marquez (POST /api/v1/lineage). O payload já sai do
/// archiver no formato do spec — a bridge encaminha o JSON CRU, sem re-serializar.
/// Commit manual só após 2xx: Marquez fora do ar segura o offset e reentrega
/// (at-least-once; o RunEvent é idempotente por runId). Sem Marquez:BaseUrl
/// configurado, a bridge nem sobe — o tópico continua sendo a fonte da verdade.
/// </summary>
public sealed partial class LineageBridge(
    ArchiverOptions options,
    IConfiguration config,
    ILogger<LineageBridge> log) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // 5xx persistente no MESMO evento não é "Marquez fora do ar", é evento-veneno
    // (ex.: NPE do Marquez em facet fora do spec): depois do teto, registra e segue —
    // um evento ruim não pode represar a linhagem de todos os outros.
    private const int MaxAttemptsPerEvent = 8;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = config["Marquez:BaseUrl"];
        if (string.IsNullOrEmpty(baseUrl))
        {
            LogDisabled();
            return Task.CompletedTask;
        }

        return Task.Run(() => PumpAsync(baseUrl, stoppingToken), stoppingToken);
    }

    private async Task PumpAsync(string baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.KafkaBootstrap,
            GroupId = "lineage-marquez",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest, // linhagem histórica também interessa ao grafo
        }).Build();
        consumer.Subscribe(options.LineageTopic);
        LogStarted(options.LineageTopic, baseUrl);

        TopicPartitionOffset? retryingOffset = null;
        var attempts = 0;

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string> result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException)
            {
                continue;
            }

            try
            {
                using var content = new StringContent(result.Message.Value, System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(new Uri("/api/v1/lineage", UriKind.Relative), content, ct);

                if (response.IsSuccessStatusCode)
                {
                    consumer.Commit(result);
                    ArchiverTelemetry.LineageDelivered.Add(1);
                    (retryingOffset, attempts) = (null, 0);
                    continue;
                }

                // 4xx = evento que o Marquez nunca vai aceitar: registra e segue (não trava a partição).
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    LogRejected((int)response.StatusCode, result.Message.Key);
                    consumer.Commit(result);
                    ArchiverTelemetry.LineageRejected.Add(1);
                    (retryingOffset, attempts) = (null, 0);
                    continue;
                }

                LogUnavailable((int)response.StatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                LogDown(e);
            }

            attempts = result.TopicPartitionOffset.Equals(retryingOffset) ? attempts + 1 : 1;
            retryingOffset = result.TopicPartitionOffset;

            if (attempts >= MaxAttemptsPerEvent)
            {
                LogPoison(result.Message.Key, attempts);
                consumer.Commit(result);
                ArchiverTelemetry.LineageRejected.Add(1);
                (retryingOffset, attempts) = (null, 0);
                continue;
            }

            // 5xx/timeout/queda: sem commit — reentrega depois do respiro.
            consumer.Seek(result.TopicPartitionOffset);
            await Task.Delay(RetryDelay, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge de linhagem desligada (Marquez:BaseUrl vazio) — o tópico segue como fonte da verdade")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge de linhagem: {Topic} → {BaseUrl}/api/v1/lineage")]
    private partial void LogStarted(string topic, string baseUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marquez recusou o RunEvent ({Status}) — objeto {Key} fica sem linhagem no grafo")]
    private partial void LogRejected(int status, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marquez indisponível ({Status}) — offset segurado pra reentrega")]
    private partial void LogUnavailable(int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marquez fora do ar — offset segurado pra reentrega")]
    private partial void LogDown(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Evento-veneno após {Attempts} tentativas — objeto {Key} fica sem linhagem no grafo; seguindo")]
    private partial void LogPoison(string key, int attempts);
}
