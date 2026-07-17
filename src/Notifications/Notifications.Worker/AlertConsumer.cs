using Confluent.Kafka;
using Notifications.Domain.Alerts;
using Notifications.Domain.Escalation;
using Platform.ServiceDefaults;

namespace Notifications.Worker;

/// <summary>
/// Consome linha.alertas.v1 e despacha pelo canal que a severidade manda.
/// Ack/escalonamento contínuo (re-notificar o próximo degrau) roda no timer:
/// a cada tick, realerta quem a política disser — estado de ack em memória
/// na fase 0, Valkey na fase 1.
/// </summary>
public sealed partial class AlertConsumer(
    NtfyPusher pusher,
    EmailSender email,
    EscalationPolicy escalation,
    ServiceInstrumentation instrumentation,
    IConfiguration config,
    ILogger<AlertConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config["Kafka:Bootstrap"] ?? "localhost:9092",
            GroupId = "notifications",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(config["Kafka:AlertsTopic"] ?? "linha.alertas.v1");
        var dispatched = instrumentation.Meter.CreateCounter<long>("notifications.dispatched");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            using var activity = instrumentation.Activity.StartActivity("notifications.dispatch");

            var alert = AlertJson.TryParse(result.Message.Value);
            if (alert is null)
            {
                LogMalformed(result.Message.Value.Length);
                consumer.Commit(result); // malformado não bloqueia a partição; schema registry evita isso na origem
                continue;
            }

            activity?.SetTag("alert.severity", alert.Severity.ToString());
            var decision = escalation.Decide(alert.Severity, DateTimeOffset.UtcNow - alert.RaisedAt, acknowledged: false);

            foreach (var channel in ChannelRouter.ChannelsFor(alert.Severity))
            {
                var target = decision.NotifyNow ?? "plantao";
                await (channel switch
                {
                    Channel.Push => pusher.PushAsync(target, alert, stoppingToken),
                    _ => email.SendAsync(target, alert, stoppingToken),
                });
                dispatched.Add(1,
                    new KeyValuePair<string, object?>("channel", channel.ToString()),
                    new KeyValuePair<string, object?>("severity", alert.Severity.ToString()));
            }

            consumer.Commit(result);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerta malformado descartado ({Bytes} bytes) — schema gate furou?")]
    private partial void LogMalformed(int bytes);
}

/// <summary>Push OSS self-hosted: POST http://ntfy/{topic}. O app Tauri assina o tópico do contato.</summary>
public sealed class NtfyPusher(HttpClient http)
{
    public async Task PushAsync(string contact, AlertMessage alert, CancellationToken ct)
    {
        using var content = new StringContent(alert.Body);
        content.Headers.Add("X-Title", alert.Title);
        content.Headers.Add("X-Priority", alert.Severity == Severity.Critical ? "urgent" : "default");
        using var response = await http.PostAsync(new Uri($"/{contact}", UriKind.Relative), content, ct);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Fase 0: e-mail vira log estruturado. Fase 1: SMTP relay interno.</summary>
public sealed partial class EmailSender(ILogger<EmailSender> log)
{
    public Task SendAsync(string contact, AlertMessage alert, CancellationToken ct)
    {
        LogEmail(contact, alert.Title, alert.Severity);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail para {Contact}: {Title} [{Severity}]")]
    private partial void LogEmail(string contact, string title, Severity severity);
}
