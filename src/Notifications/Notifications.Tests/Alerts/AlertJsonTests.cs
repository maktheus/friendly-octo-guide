using Notifications.Domain.Alerts;
using Notifications.Domain.Escalation;
using Xunit;

namespace Notifications.Tests.Alerts;

public class AlertJsonTests
{
    [Fact]
    public void Payload_do_predictive_com_severidade_string_e_aceito()
    {
        // Formato exato que o ScoringLoop publica em linha.alertas.v1.
        const string json = """
            {"id":"5c9f2c1e-8f4a-4d31-9c8e-2b7d6a1f0e3d",
             "title":"Anomalia em sensor-7",
             "body":"Valor 141.20 está a 5.3 desvios do regime.",
             "severity":"Warning",
             "raisedAt":"2026-07-17T12:00:00+00:00"}
            """;

        var alert = AlertJson.TryParse(json);

        Assert.NotNull(alert);
        Assert.Equal(Severity.Warning, alert.Severity);
        Assert.Equal("Anomalia em sensor-7", alert.Title);
    }

    [Fact]
    public void Malformado_devolve_null_em_vez_de_derrubar_o_consumidor()
    {
        Assert.Null(AlertJson.TryParse("isto não é json"));
        Assert.Null(AlertJson.TryParse("""{"severity":"Apocalyptic"}""")); // enum desconhecido
        Assert.Null(AlertJson.TryParse("""{"severity":42e999}"""));
    }

    [Fact]
    public void Ida_e_volta_preserva_o_alerta()
    {
        var original = new AlertMessage(
            Guid.NewGuid(), "titulo", "corpo", Severity.Critical,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

        var roundTripped = AlertJson.TryParse(AlertJson.Serialize(original));

        Assert.Equal(original, roundTripped);
    }
}
