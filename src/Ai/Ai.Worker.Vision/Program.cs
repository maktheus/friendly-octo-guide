using Ai.Domain.Jobs;
using Ai.Worker.Runtime;
using Ai.Worker.Vision;
using Platform.ServiceDefaults;
using StackExchange.Redis;

// Worker de visão/OCR: um Deployment K8s isolado, GPU node pool, scale-to-zero via
// KEDA por lag de ai.jobs.vision.v1. Serving auto-hospedado — zero custo por token.

var instrumentation = new ServiceInstrumentation("ai-worker-vision");

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

builder.Services.AddHttpClient<IJobProcessor, VisionProcessor>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Vision:BaseUrl"] ?? "http://localhost:8001");
    c.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddHostedService<AiJobLoop>();

await builder.Build().RunAsync();
