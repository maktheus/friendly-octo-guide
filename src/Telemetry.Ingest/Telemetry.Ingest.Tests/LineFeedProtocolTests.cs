using System.Text.Json;
using Telemetry.Ingest.Domain.LineFeed;
using Xunit;

namespace Telemetry.Ingest.Tests;

public class LineFeedProtocolTests
{
    [Fact]
    public void Frame_da_pwa_entrega_o_token()
    {
        // Shape exato que clients/pwa/app.js envia no open do socket.
        var token = LineFeedProtocol.BearerTokenFrom("""{"authorization":"Bearer abc.def.ghi"}""");
        Assert.Equal("abc.def.ghi", token);
    }

    [Theory]
    [InlineData("""{"Authorization":"Bearer t"}""")] // caixa não importa
    [InlineData("""{"AUTHORIZATION":"bearer t"}""")]
    public void Caixa_do_campo_e_do_scheme_nao_importa(string frame)
    {
        Assert.Equal("t", LineFeedProtocol.BearerTokenFrom(frame));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao é json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"authorization":42}""")]
    [InlineData("""{"authorization":"Basic dXNlcg=="}""")] // scheme errado
    [InlineData("""{"authorization":"Bearer "}""")]        // token vazio
    [InlineData("""{"outra":"coisa"}""")]
    public void Frame_invalido_devolve_null_e_o_host_fecha_o_socket(string frame)
    {
        Assert.Null(LineFeedProtocol.BearerTokenFrom(frame));
    }

    [Fact]
    public void Frame_de_leitura_tem_o_shape_que_o_painel_consome()
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var frame = LineFeedProtocol.ReadingFrame("temp-forno-01", 141.5, at);

        using var doc = JsonDocument.Parse(frame);
        Assert.Equal("temp-forno-01", doc.RootElement.GetProperty("sensorId").GetString());
        Assert.Equal(141.5, doc.RootElement.GetProperty("value").GetDouble());
        Assert.Equal(at, doc.RootElement.GetProperty("measuredAt").GetDateTimeOffset());
    }
}
