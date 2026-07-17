using System.Text.Json;
using System.Text.Json.Serialization;
using Notifications.Domain.Escalation;

namespace Notifications.Domain.Alerts;

/// <summary>Envelope de linha.alertas.v1 como o Predictive publica. DTO puro.</summary>
public sealed record AlertMessage(Guid Id, string Title, string Body, Severity Severity, DateTimeOffset RaisedAt);

/// <summary>
/// Parsing do envelope JSON de linha.alertas.v1. A severidade trafega como STRING
/// ("Warning") — sem o conversor de enum, cada alerta viraria JsonException no
/// consumidor, que morreria sem commitar o offset e voltaria pra mesma mensagem
/// (crash-loop). Malformado devolve null: quem consome loga e segue — payload ruim
/// nunca trava a partição.
/// </summary>
public static class AlertJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static AlertMessage? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AlertMessage>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(AlertMessage alert) => JsonSerializer.Serialize(alert, Options);
}
