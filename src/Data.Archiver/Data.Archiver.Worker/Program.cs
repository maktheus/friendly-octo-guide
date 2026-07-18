using System.Diagnostics;
using System.Diagnostics.Metrics;
using Amazon.S3;
using Data.Archiver.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// Big data pool (3b): tudo que passa pelo tópico de telemetria vira objeto JSONL.gz
// no MinIO/S3, particionado Hive-style — a matéria-prima de treino/analytics fica
// fora do Postgres quente, com custo de armazenamento de lake.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("service", ArchiverTelemetry.ServiceName)
    .WriteTo.Console(formatProvider: null));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ArchiverTelemetry.ServiceName))
    .WithTracing(t => t
        .AddSource(ArchiverTelemetry.ServiceName)
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddMeter(ArchiverTelemetry.ServiceName)
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

var options = ArchiverOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);

// Credenciais pelas variáveis padrão AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY —
// mesmo contrato pra MinIO no compose e S3 de verdade no cluster.
builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(new AmazonS3Config
{
    ServiceURL = options.S3Endpoint,
    ForcePathStyle = true, // MinIO não faz virtual-host buckets
}));
builder.Services.AddSingleton(sp => new S3ObjectStore(sp.GetRequiredService<IAmazonS3>(), options.Bucket));
builder.Services.AddHostedService<ArchiverConsumer>();
builder.Services.AddHostedService<LineageBridge>(); // linhagem → Marquez (liga com Marquez:BaseUrl)

await builder.Build().RunAsync();

namespace Data.Archiver.Worker
{
    /// <summary>Fonte única de nomes de instrumentação do serviço.</summary>
    public static class ArchiverTelemetry
    {
        public const string ServiceName = "data-archiver";
        public static readonly ActivitySource Activity = new(ServiceName);
        public static readonly Meter Meter = new(ServiceName);

        public static readonly Counter<long> Records =
            Meter.CreateCounter<long>("archiver.records");
        public static readonly Counter<long> Objects =
            Meter.CreateCounter<long>("archiver.objects");
        public static readonly Counter<long> Bytes =
            Meter.CreateCounter<long>("archiver.bytes", unit: "By");
        public static readonly Counter<long> LineageDelivered =
            Meter.CreateCounter<long>("archiver.lineage.delivered");
        public static readonly Counter<long> LineageRejected =
            Meter.CreateCounter<long>("archiver.lineage.rejected");
    }

    public sealed record ArchiverOptions(
        string KafkaBootstrap,
        string Topic,
        string S3Endpoint,
        string Bucket,
        int MaxRecordsPerObject,
        long MaxBytesPerObject,
        TimeSpan MaxBatchAge,
        TimeSpan WormRetention,
        string LineageTopic,
        string SchemaVersion,
        string SourceProducer)
    {
        public static ArchiverOptions From(IConfiguration cfg) => new(
            cfg["Kafka:Bootstrap"] ?? "localhost:9092",
            cfg["Kafka:TelemetryTopic"] ?? "linha.telemetria.v1",
            cfg["S3:Endpoint"] ?? "http://localhost:9000",
            cfg["S3:Bucket"] ?? "linha-lake",
            cfg.GetValue("Archiver:MaxRecordsPerObject", 5000),
            cfg.GetValue("Archiver:MaxBytesPerObject", 8L * 1024 * 1024),
            TimeSpan.FromSeconds(cfg.GetValue("Archiver:MaxBatchAgeSeconds", 60)),
            // WORM: retenção de imutabilidade do objeto (0 = desligado em dev; anos em prod).
            TimeSpan.FromDays(cfg.GetValue("Archiver:WormRetentionDays", 0)),
            cfg["Lineage:Topic"] ?? "linhagem.openlineage.v1",
            cfg["Lineage:SchemaVersion"] ?? "sensor-reading.v1",
            cfg["Lineage:SourceProducer"] ?? "telemetry-ingest");
    }
}
