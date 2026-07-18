using System.Net.Http.Json;
using System.Text.Json;
using Ai.Domain.Jobs;
using Ai.Worker.Runtime;

namespace Ai.Worker.Vision;

/// <summary>
/// Worker de visão: OCR e classificação de imagem sobre um endpoint auto-hospedado
/// (mesmo contrato de serving do GPU pool). O payload do job traz a imagem por
/// referência (URL no MinIO/data lake) e a tarefa; o resultado é texto (OCR) ou
/// rótulos (classificação). Pod isolado por tipo de modelo, como o Router espera.
/// </summary>
public sealed class VisionProcessor(HttpClient http, IConfiguration config) : IJobProcessor
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string ConsumerGroup => "ai-worker-vision";
    public string JobsTopic => config["Kafka:VisionJobsTopic"] ?? "ai.jobs.vision.v1";

    public async Task<object> ProcessAsync(AiJob job, CancellationToken ct)
    {
        // Payload malformado vira InvalidOperationException (que o loop trata: retry → DLQ),
        // não JsonException crua que derrubaria o worker.
        VisionRequest request;
        try
        {
            request = JsonSerializer.Deserialize<VisionRequest>(job.Payload, JsonOpts)
                ?? throw new InvalidOperationException("Payload de visão vazio.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Payload de visão malformado.", ex);
        }

        var body = new
        {
            model = config["Vision:Model"] ?? "florence-2",
            task = request.Task,        // "ocr" | "caption" | "classify"
            image_url = request.ImageUrl,
        };

        using var response = await http.PostAsJsonAsync("/v1/vision", body, JsonOpts, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VisionResult>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Endpoint de visão devolveu corpo vazio.");

        // XAI é contrato, não cortesia (épico #9): classificação de defeito sem
        // explicação não entra na linha — o resultado carrega o porquê e a confiança.
        return new
        {
            request.Task,
            text = result.Text,
            labels = result.Labels ?? [],
            confidence = result.Confidence,
            explanation = result.Explanation,
        };
    }

    private sealed record VisionRequest(string Task, string ImageUrl);
    private sealed record VisionResult(
        string? Text, IReadOnlyList<string>? Labels, double? Confidence = null, string? Explanation = null);
}
