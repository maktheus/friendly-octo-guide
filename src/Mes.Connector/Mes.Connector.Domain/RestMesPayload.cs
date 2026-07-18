using System.Text.Json;

namespace Mes.Connector.Domain;

/// <summary>
/// Parsing do payload REST do MES (épico #6, adapter real): um array JSON de linhas
/// cruas, camelCase, com cursor/ativoId/tipo/codigo obrigatórios. Puro — o adapter
/// HTTP só busca bytes; o contrato mora aqui, testável sem rede. Payload que não é
/// array ou linha sem os obrigatórios é FormatException: contrato do adapter
/// quebrado grita, não perde linha calado.
/// </summary>
public static class RestMesPayload
{
    public static IReadOnlyList<RawMesRow> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Resposta do MES não é JSON válido.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new FormatException("Resposta do MES deveria ser um array de eventos.");

            var rows = new List<RawMesRow>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                rows.Add(new RawMesRow(
                    Cursor: Required(el, "cursor"),
                    AtivoId: Required(el, "ativoId"),
                    Tipo: Required(el, "tipo"),
                    Codigo: Required(el, "codigo"),
                    Quantidade: Optional(el, "quantidade"),
                    Texto: Optional(el, "texto"),
                    Turno: Optional(el, "turno"),
                    OccurredAt: Optional(el, "occurredAt") ?? ""));
            }

            return rows;
        }
    }

    private static string Required(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : throw new FormatException($"Evento MES sem o campo obrigatório '{name}'.");

    private static string? Optional(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
