using Ai.Domain.Jobs;
using Ai.Worker.Llm;
using Ai.Worker.Runtime;
using Platform.ServiceDefaults;
using StackExchange.Redis;

// Worker de LLM: um Deployment K8s por modelo, GPU node pool, scale-to-zero via KEDA.
// Inferência no vLLM auto-hospedado (API OpenAI-compatible) — zero custo por token.

var instrumentation = new ServiceInstrumentation("ai-worker-llm");

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

builder.Services.AddHttpClient<VllmClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Vllm:BaseUrl"] ?? "http://localhost:8000");
    c.Timeout = TimeSpan.FromMinutes(5); // inferência longa não é timeout de rede
});
builder.Services.AddHostedService<LlmJobLoop>();

await builder.Build().RunAsync();
