using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults;
using Serilog;
using Telemetry.Ingest.Worker;

// Template de host do monorepo: todo serviço nasce com log estruturado,
// traces e métricas apontando pro OTel Collector — a "espinha" recebe
// desde o primeiro deploy, sem exceção. Host web (não worker puro) porque o
// painel ao vivo da PWA conecta aqui: /v1/linha/ws via Gateway.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("service", IngestTelemetry.ServiceName)
    .WriteTo.Console(formatProvider: null)); // JSON via appsettings em prod

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(IngestTelemetry.ServiceName))
    .WithTracing(t => t
        .AddSource(IngestTelemetry.ServiceName)
        .AddOtlpExporter()) // OTEL_EXPORTER_OTLP_ENDPOINT via env
    .WithMetrics(m => m
        .AddMeter(IngestTelemetry.ServiceName)
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

// O hub valida o token do painel com a mesma chave do Identity (fallback de dev
// só em Development — fora dele o boot falha sem OpenBao, como nos outros hosts).
builder.Configuration["Jwt:SigningKey"] = PlatformSecrets.JwtSigningKey(builder.Configuration, builder.Environment);

builder.Services.AddSingleton(IngestOptions.From(builder.Configuration));
builder.Services.AddSingleton<ReadingSink>();
builder.Services.AddSingleton<LineFeedHub>();
builder.Services.AddHostedService<IngestConsumer>();
builder.Services.AddHostedService<LineFeedBroadcaster>();

var app = builder.Build();

app.UseWebSockets();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Map("/v1/linha/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    await context.RequestServices.GetRequiredService<LineFeedHub>().HandleAsync(context);
});

await app.RunAsync();

namespace Telemetry.Ingest.Worker
{
    /// <summary>Fonte única de nomes de instrumentação do serviço.</summary>
    public static class IngestTelemetry
    {
        public const string ServiceName = "telemetry-ingest";
        public static readonly ActivitySource Activity = new(ServiceName);
        public static readonly Meter Meter = new(ServiceName);

        // As três métricas que o painel da linha consome:
        public static readonly Counter<long> Accepted =
            Meter.CreateCounter<long>("ingest.readings.accepted");
        public static readonly Counter<long> Quarantined =
            Meter.CreateCounter<long>("ingest.readings.quarantined");
        public static readonly Histogram<double> LagSeconds =
            Meter.CreateHistogram<double>("ingest.lag.seconds");
    }

    public sealed record IngestOptions(
        string KafkaBootstrap,
        string TelemetryTopic,
        string QuarantineTopic,
        string PostgresConnection)
    {
        public static IngestOptions From(IConfiguration cfg) => new(
            cfg["Kafka:Bootstrap"] ?? "localhost:9092",
            cfg["Kafka:TelemetryTopic"] ?? "linha.telemetria.v1",
            cfg["Kafka:QuarantineTopic"] ?? "linha.telemetria.quarentena.v1",
            cfg.GetConnectionString("Postgres") ?? "Host=localhost;Database=linha;Username=dev;Password=dev");
    }
}
