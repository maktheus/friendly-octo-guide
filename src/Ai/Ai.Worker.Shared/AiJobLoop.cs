using System.Diagnostics;
using System.Text.Json;
using Ai.Domain.Jobs;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.ServiceDefaults;

namespace Ai.Worker.Runtime;

/// <summary>
/// Loop genérico dos workers de IA (visão, embeddings, …): consome o tópico do
/// processor → processa → ai.resultados.v1. Idempotência por job-id (reprocesso
/// nunca duplica resultado); falha reencaminha o job com attempts+1 pro funil do
/// router, que decide retry ou DLQ. Job nunca é perdido: só commita o offset
/// depois de concluir OU reenfileirar.
/// </summary>
public sealed partial class AiJobLoop(
    IJobProcessor processor,
    ServiceInstrumentation instrumentation,
    IConfiguration config,
    ILogger<AiJobLoop> log,
    IIdempotencyLedger? ledger = null) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyLedger _ledger = ledger ?? new InMemoryIdempotencyLedger();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrap = config["Kafka:Bootstrap"] ?? "localhost:9092";
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = processor.ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap,
            EnableIdempotence = true,
        }).Build();

        consumer.Subscribe(processor.JobsTopic);
        var inferenceSeconds = instrumentation.Meter.CreateHistogram<double>("ai.inference.seconds");
        var failures = instrumentation.Meter.CreateCounter<long>("ai.inference.failures");
        LogConsuming(processor.JobsTopic, processor.ConsumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            using var activity = instrumentation.Activity.StartActivity("ai.job");

            var job = JsonSerializer.Deserialize<AiJob>(result.Message.Value, JsonOpts);
            if (job is null)
            {
                consumer.Commit(result); // router já teria mandado pra DLQ; cinto e suspensório
                continue;
            }

            activity?.SetTag("ai.job_id", job.JobId);
            activity?.SetTag("ai.model_type", job.ModelType);

            if (await _ledger.TryClaimAsync(job.JobId, stoppingToken) != JobClaim.Accepted)
            {
                consumer.Commit(result); // duplicata de entrega — resultado já existe ou está em voo
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var output = await processor.ProcessAsync(job, stoppingToken);
                inferenceSeconds.Record(stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("model_type", job.ModelType));

                await producer.ProduceAsync(config["Kafka:ResultsTopic"] ?? "ai.resultados.v1",
                    new Message<string, string>
                    {
                        Key = job.JobId.ToString(),
                        Value = JsonSerializer.Serialize(new { jobId = job.JobId, modelType = job.ModelType, output }, JsonOpts),
                    }, stoppingToken);

                await _ledger.CompleteAsync(job.JobId, stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                failures.Add(1, new KeyValuePair<string, object?>("model_type", job.ModelType));
                await _ledger.ReleaseAsync(job.JobId, stoppingToken); // devolve o claim: retry é legítimo
                LogProcessingFailed(ex, job.JobId, job.Attempts + 1);

                var retry = job with { Attempts = job.Attempts + 1 };
                await producer.ProduceAsync(config["Kafka:JobsTopic"] ?? "ai.jobs.v1",
                    new Message<string, string>
                    {
                        Key = retry.JobId.ToString(),
                        Value = JsonSerializer.Serialize(retry, JsonOpts),
                    }, stoppingToken);
            }

            consumer.Commit(result); // concluído OU reenfileirado — nunca perdido
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumindo {Topic} no grupo {Group}")]
    private partial void LogConsuming(string topic, string group);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processamento falhou pro job {JobId} (tentativa {Attempt})")]
    private partial void LogProcessingFailed(Exception ex, Guid jobId, int attempt);
}
