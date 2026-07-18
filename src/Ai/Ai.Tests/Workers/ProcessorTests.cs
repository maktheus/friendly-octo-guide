using System.Net;
using System.Text;
using Ai.Domain.Jobs;
using Ai.Worker.Embeddings;
using Ai.Worker.Vision;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ai.Tests.Workers;

/// <summary>
/// Os processors de visão e embeddings testados sem GPU: um handler fake devolve a
/// resposta do endpoint de serving. O que está sob teste é o contrato — parsing do
/// payload do job, forma da chamada e da resposta — não o modelo em si.
/// </summary>
public class ProcessorTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => respond(request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
    }

    private static HttpClient Client(Func<HttpRequestMessage, string, HttpResponseMessage> respond) =>
        new(new FakeHandler(respond)) { BaseAddress = new Uri("http://serving.test") };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static IConfiguration Cfg() => new ConfigurationBuilder().Build();

    private static AiJob Job(string payload) => new(Guid.NewGuid(), "vision", payload, 0);

    [Fact]
    public async Task Vision_ocr_extrai_texto_do_endpoint()
    {
        string? sentTask = null;
        var proc = new VisionProcessor(Client((_, body) =>
        {
            sentTask = body.Contains("\"ocr\"", StringComparison.Ordinal) ? "ocr" : null;
            return Json("""{"text":"LOTE 4471","labels":null}""");
        }), Cfg());

        var result = await proc.ProcessAsync(Job("""{"task":"ocr","imageUrl":"s3://lake/img/1.png"}"""), default);

        Assert.Equal("ocr", sentTask);
        Assert.Contains("LOTE 4471", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vision_classificacao_repassa_confianca_e_explicacao_do_serving()
    {
        // XAI é contrato (épico #9): o resultado carrega o porquê e a confiança do modelo.
        var proc = new VisionProcessor(Client((_, _) =>
            Json("""{"text":null,"labels":["SOLDER-BRIDGE"],"confidence":0.87,"explanation":"continuidade de pasta no pad 14"}""")), Cfg());

        var result = await proc.ProcessAsync(
            Job("""{"task":"classify","imageUrl":"s3://lake/inspecao/placa-4471.png"}"""), default);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("SOLDER-BRIDGE", json, StringComparison.Ordinal);
        Assert.Contains("0.87", json, StringComparison.Ordinal);
        Assert.Contains("pad 14", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vision_payload_malformado_falha_para_o_loop_reencaminhar()
    {
        var proc = new VisionProcessor(Client((_, _) => Json("{}")), Cfg());
        await Assert.ThrowsAsync<InvalidOperationException>(() => proc.ProcessAsync(Job("não-é-json"), default));
    }

    [Fact]
    public async Task Vision_consome_o_topico_de_visao()
    {
        var proc = new VisionProcessor(Client((_, _) => Json("{}")), Cfg());
        Assert.Equal("ai.jobs.vision.v1", proc.JobsTopic);
        Assert.Equal("ai-worker-vision", proc.ConsumerGroup);
    }

    [Fact]
    public async Task Embeddings_vetoriza_e_reporta_dimensao()
    {
        var proc = new EmbeddingsProcessor(Client((_, _) =>
            Json("""{"data":[{"embedding":[0.1,0.2,0.3]},{"embedding":[0.4,0.5,0.6]}]}""")), Cfg());

        var result = await proc.ProcessAsync(
            new AiJob(Guid.NewGuid(), "embedding", """{"input":["olá","mundo"]}""", 0), default);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"count\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"dimensions\":3", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embeddings_sem_entrada_e_recusado()
    {
        var proc = new EmbeddingsProcessor(Client((_, _) => Json("""{"data":[]}""")), Cfg());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proc.ProcessAsync(new AiJob(Guid.NewGuid(), "embedding", """{"input":[]}""", 0), default));
    }
}
