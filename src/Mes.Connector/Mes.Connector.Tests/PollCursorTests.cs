using Mes.Connector.Domain;
using Xunit;

namespace Mes.Connector.Tests;

public class PollCursorTests
{
    private static RawMesRow Row(string cursor) =>
        new(cursor, "ativo", "Apontamento", "OK");

    [Fact]
    public void Do_estado_inicial_tudo_e_novo_e_o_max_vira_o_proximo_cursor()
    {
        var (fresh, next) = PollCursor.SelectNew(
            [Row("000000000000000001"), Row("000000000000000003"), Row("000000000000000002")],
            MesPollState.Start);

        Assert.Equal(3, fresh.Count);
        Assert.Equal("000000000000000003", next.LastCursor);
    }

    [Fact]
    public void Linhas_ate_o_ultimo_cursor_sao_ignoradas()
    {
        var state = new MesPollState("000000000000000002");

        var (fresh, next) = PollCursor.SelectNew(
            [Row("000000000000000001"), Row("000000000000000002"), Row("000000000000000003")],
            state);

        Assert.Single(fresh);
        Assert.Equal("000000000000000003", fresh[0].Cursor);
        Assert.Equal("000000000000000003", next.LastCursor);
    }

    [Fact]
    public void Lote_sem_novidade_mantem_o_cursor()
    {
        var state = new MesPollState("000000000000000005");

        var (fresh, next) = PollCursor.SelectNew([Row("000000000000000004")], state);

        Assert.Empty(fresh);
        Assert.Equal("000000000000000005", next.LastCursor);
    }

    [Fact]
    public void Lote_vazio_preserva_o_estado()
    {
        var state = new MesPollState("000000000000000007");
        var (fresh, next) = PollCursor.SelectNew([], state);

        Assert.Empty(fresh);
        Assert.Equal("000000000000000007", next.LastCursor);
    }

    [Fact]
    public void Id_numerico_sem_zero_padding_nao_e_descartado_como_antigo()
    {
        // Ordinalmente "100" < "99" — o evento novo sumiria em silêncio.
        var state = new MesPollState("99");
        var (fresh, next) = PollCursor.SelectNew([Row("100")], state);

        Assert.Single(fresh);
        Assert.Equal("100", next.LastCursor);
    }

    [Theory]
    [InlineData("99", "100", -1)]                                       // numérico: 99 < 100
    [InlineData("0100", "100", 0)]                                      // zeros à esquerda não mudam o valor
    [InlineData("2026-07-17T10:00:00Z", "2026-07-17T09:59:59Z", 1)]     // ISO continua ordinal
    public void Comparacao_de_cursor_respeita_o_formato(string a, string b, int sinalEsperado)
    {
        Assert.Equal(sinalEsperado, Math.Sign(PollCursor.Compare(a, b)));
    }
}
