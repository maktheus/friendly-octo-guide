using Mes.Connector.Domain;
using Xunit;

namespace Mes.Connector.Tests;

public class RestMesPayloadTests
{
    [Fact]
    public void Array_do_mes_vira_linhas_cruas_com_opcionais_nulos()
    {
        var rows = RestMesPayload.Parse("""
            [
              {"cursor":"000101","ativoId":"linha-2","tipo":"Parada","codigo":"NOZZLE-CLOG",
               "quantidade":"1","turno":"noite","occurredAt":"2026-07-18T03:00:00Z"},
              {"cursor":"000102","ativoId":"linha-2","tipo":"Apontamento","codigo":"OK"}
            ]
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal("NOZZLE-CLOG", rows[0].Codigo);
        Assert.Equal("noite", rows[0].Turno);
        Assert.Null(rows[1].Texto);      // opcional ausente é null, não erro
        Assert.Equal("", rows[1].OccurredAt);
    }

    [Fact]
    public void Array_vazio_e_lote_sem_novidade()
    {
        Assert.Empty(RestMesPayload.Parse("[]"));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"nao":"é array"}""")]
    [InlineData("""[{"ativoId":"linha-2","tipo":"Parada","codigo":"X"}]""")] // sem cursor
    [InlineData("""[{"cursor":"1","tipo":"Parada","codigo":"X"}]""")]        // sem ativoId
    public void Contrato_quebrado_grita_em_vez_de_perder_linha_calado(string payload)
    {
        Assert.Throws<FormatException>(() => RestMesPayload.Parse(payload));
    }
}
