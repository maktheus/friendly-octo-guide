using System.Text.Json;

namespace Telemetry.Ingest.Domain.LineFeed;

/// <summary>
/// Protocolo do painel ao vivo (/v1/linha/ws). O browser NÃO envia header no
/// upgrade de WebSocket, então a credencial chega no PRIMEIRO frame:
///   {"authorization":"Bearer &lt;jwt&gt;"}
/// (é exatamente o que a PWA já envia no open). Aqui mora só o parsing — puro,
/// determinístico; a validação criptográfica do token é papel do host.
/// </summary>
public static class LineFeedProtocol
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Extrai o JWT do frame de credencial; null = frame inválido (fecha o socket).</summary>
    public static string? BearerTokenFrom(string firstFrame)
    {
        if (string.IsNullOrWhiteSpace(firstFrame))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(firstFrame);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (prop.Value.ValueKind != JsonValueKind.String)
                    return null;

                var value = prop.Value.GetString()!;
                const string scheme = "Bearer ";
                return value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
                    && value.Length > scheme.Length
                    ? value[scheme.Length..]
                    : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Frame de leitura no shape que o painel consome: {sensorId, value, measuredAt}.</summary>
    public static string ReadingFrame(string sensorId, double value, DateTimeOffset measuredAt) =>
        JsonSerializer.Serialize(new { sensorId, value, measuredAt }, Web);
}
