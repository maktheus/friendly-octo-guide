using Ai.Domain.Jobs;
using Ai.Worker.Embeddings;
using Ai.Worker.Runtime;
using Platform.ServiceDefaults;
using StackExchange.Redis;

// Worker de embeddings: Deployment K8s isolado, scale-to-zero via KEDA por lag de
// ai.jobs.embedding.v1. Alimenta pgvector/RAG — vetorização auto-hospedada.

var instrumentation = new ServiceInstrumentation("ai-worker-embeddings");

var builder = Host.CreateApplicationBuilder(args);
builder.AddPlatformDefaults(instrumentation);

builder.Services.AddSingleton(instrumentation);

var valkeyEndpoint = builder.Configuration.GetConnectionString("Valkey");
if (!string.IsNullOrEmpty(valkeyEndpoint))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect($"{valkeyEndpoint},abortConnect=false,connectRetry=5"));
    builder.Services.AddSingleton<IIdempotencyLedger>(sp => new ValkeyIdempotencyLedger(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        sp.GetRequiredService<ILogger<ValkeyIdempotencyLedger>>(),
        instrumentation.Meter));
}
else
{
    builder.Services.AddSingleton<IIdempotencyLedger, InMemoryIdempotencyLedger>();
}

builder.Services.AddHttpClient<IJobProcessor, EmbeddingsProcessor>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Embeddings:BaseUrl"] ?? "http://localhost:8002");
    c.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHostedService<AiJobLoop>();

await builder.Build().RunAsync();
