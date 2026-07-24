using System.Diagnostics;
using System.Diagnostics.Metrics;
using Mes.Connector.Domain;
using Mes.Connector.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// Conector MES (nível Purdue 3/4): poll do MES → normaliza → Kafka mes.eventos.v1.
// Caminho próprio, paralelo ao Edge.ProtocolGateway (chão de fábrica). Template de
// host do monorepo: nasce com log estruturado, traces e métricas no OTel Collector.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("service", MesTelemetry.ServiceName)
    .WriteTo.Console(formatProvider: null));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(MesTelemetry.ServiceName))
    .WithTracing(t => t
        .AddSource(MesTelemetry.ServiceName)
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddMeter(MesTelemetry.ServiceName)
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

builder.Services.AddSingleton(MesOptions.From(builder.Configuration));
builder.Services.AddSingleton<MesEventSink>();

// Cursor durável com Postgres configurado; sem ele, memória (fase 0 explícita).
var pgConn = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrEmpty(pgConn))
    builder.Services.AddSingleton<ICursorStore>(new PostgresCursorStore(pgConn));
else
    builder.Services.AddSingleton<ICursorStore, InMemoryCursorStore>();

// Adapter: Mes:Rest:BaseUrl liga o MES real via REST; Mes:Sql:ConnectionString via SQL;
// sem elas, simulador de dev.
var mesRestBaseUrl = builder.Configuration["Mes:Rest:BaseUrl"];
var mesSqlConnStr = builder.Configuration.GetConnectionString("MesSql") ?? builder.Configuration["Mes:Sql:ConnectionString"];

if (!string.IsNullOrEmpty(mesRestBaseUrl))
{
    builder.Services.AddHttpClient<IMesAdapter, RestMesAdapter>(c =>
    {
        c.BaseAddress = new Uri(mesRestBaseUrl.EndsWith('/') ? mesRestBaseUrl : mesRestBaseUrl + "/");
        c.Timeout = TimeSpan.FromSeconds(15);
        if (builder.Configuration["Mes:Rest:ApiKey"] is { Length: > 0 } apiKey)
            c.DefaultRequestHeaders.Add("X-Api-Key", apiKey); // em prod a chave vem do OpenBao
    });
}
else if (!string.IsNullOrEmpty(mesSqlConnStr))
{
    var providerName = builder.Configuration["Mes:Sql:ProviderName"];
    var query = builder.Configuration["Mes:Sql:Query"];
    builder.Services.AddSingleton<IMesAdapter>(new SqlMesAdapter(mesSqlConnStr, providerName, query));
}
else
{
    builder.Services.AddSingleton<IMesAdapter, SimulatorMesAdapter>();
}

builder.Services.AddHostedService<MesConnectorWorker>();

var host = builder.Build();
if (host.Services.GetRequiredService<ICursorStore>() is PostgresCursorStore durable)
    await durable.EnsureSchemaAsync(CancellationToken.None);

await host.RunAsync();

namespace Mes.Connector.Worker
{
    /// <summary>Fonte única de nomes de instrumentação do serviço.</summary>
    public static class MesTelemetry
    {
        public const string ServiceName = "mes-connector";
        public static readonly ActivitySource Activity = new(ServiceName);
        public static readonly Meter Meter = new(ServiceName);

        public static readonly Counter<long> Published =
            Meter.CreateCounter<long>("mes.events.published");
        public static readonly Counter<long> Quarantined =
            Meter.CreateCounter<long>("mes.events.quarantined");
    }

    public sealed record MesOptions(
        string KafkaBootstrap,
        string EventTopic,
        string QuarantineTopic,
        string SourceSystem,
        TimeSpan PollInterval)
    {
        public static MesOptions From(IConfiguration cfg) => new(
            cfg["Kafka:Bootstrap"] ?? "localhost:9092",
            cfg["Kafka:EventTopic"] ?? "mes.eventos.v1",
            cfg["Kafka:QuarantineTopic"] ?? "mes.quarentena.v1",
            cfg["Mes:SourceSystem"] ?? "simulador",
            TimeSpan.FromSeconds(cfg.GetValue("Mes:PollIntervalSeconds", 5)));
    }
}
