using Gateway.Domain;
using Platform.AccessControl;
using Xunit;

namespace Gateway.Tests;

public class RouteTableTests
{
    private static readonly RouteTable Table = new RouteTable()
        .Public("/v1/auth")
        .Require("/v1/core", RouteRequirement.ForRoles("operador", "admin"))
        .Require("/v1/core/admin", RouteRequirement.ForRoles("admin"))
        .Require("/v1/linha", new RouteRequirement(
            new HashSet<string> { "operador" },
            new Dictionary<string, string> { ["linha"] = "*" }))
        .Public("/v1/linha/ws"); // upgrade WS: browser não manda header; o hub valida o 1º frame

    [Fact]
    public void Rota_nao_listada_e_negada_por_padrao()
    {
        var match = Table.Match("/v1/interno/debug");
        Assert.False(match.IsListed);
    }

    [Fact]
    public void Rota_publica_passa_sem_exigencia()
    {
        var match = Table.Match("/v1/auth/login");
        Assert.True(match.IsPublic);
    }

    [Fact]
    public void Prefixo_mais_longo_vence()
    {
        // /v1/core/admin exige admin, mesmo /v1/core aceitando operador.
        var match = Table.Match("/v1/core/admin/usuarios");
        Assert.Equal(new HashSet<string> { "admin" }, match.Requirement!.AnyOfRoles);
    }

    [Fact]
    public void Prefixo_nao_casa_no_meio_de_segmento()
    {
        // /v1/corex NÃO é /v1/core.
        var match = Table.Match("/v1/corex/dados");
        Assert.False(match.IsListed);
    }

    [Fact]
    public void Match_e_indiferente_a_barras_e_caixa()
    {
        var match = Table.Match("V1/CORE/");
        Assert.True(match.IsListed);
        Assert.False(match.IsPublic);
    }

    [Fact]
    public void Upgrade_do_websocket_do_painel_e_publico_na_borda()
    {
        // O browser não envia Authorization no upgrade; a credencial vai no primeiro
        // frame e o hub valida. A borda só encaminha ESTE caminho — o resto de
        // /v1/linha continua exigindo papel.
        Assert.True(Table.Match("/v1/linha/ws").IsPublic);
    }

    [Fact]
    public void Resto_de_linha_continua_exigindo_papel()
    {
        var match = Table.Match("/v1/linha/historico");
        Assert.True(match.IsListed);
        Assert.False(match.IsPublic);
    }
}

public class ClaimsMapperTests
{
    [Fact]
    public void Claims_do_jwt_viram_subject_com_papeis_e_atributos()
    {
        var subject = ClaimsMapper.ToSubject("maria", [
            ("role", "operador"),
            ("role", "qualidade"),
            ("attr:planta", "A"),
            ("attr:linha", "2"),
            ("aud", "plataforma-linha"), // claim irrelevante é ignorada
        ]);

        Assert.Equal("maria", subject.UserId);
        Assert.True(subject.Roles.SetEquals(["operador", "qualidade"]));
        Assert.Equal("A", subject.Attributes["planta"]);
        Assert.Equal("2", subject.Attributes["linha"]);
        Assert.Equal(2, subject.Attributes.Count);
    }
}

public class TokenBucketTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    [Fact]
    public void Burst_ate_a_capacidade_e_permitido()
    {
        var bucket = new TokenBucket(capacity: 3, refillPerSecond: 1);
        Assert.True(bucket.TryTake(T0));
        Assert.True(bucket.TryTake(T0));
        Assert.True(bucket.TryTake(T0));
        Assert.False(bucket.TryTake(T0)); // 4ª no mesmo instante estoura
    }

    [Fact]
    public void Tokens_reabastecem_com_o_tempo()
    {
        var bucket = new TokenBucket(capacity: 1, refillPerSecond: 1);
        Assert.True(bucket.TryTake(T0));
        Assert.False(bucket.TryTake(T0));
        Assert.True(bucket.TryTake(T0 + TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public void Reabastecimento_nao_passa_da_capacidade()
    {
        var bucket = new TokenBucket(capacity: 2, refillPerSecond: 10);
        Assert.True(bucket.TryTake(T0));

        // Uma hora depois: só 2 tokens, não 36 mil.
        var later = T0 + TimeSpan.FromHours(1);
        Assert.True(bucket.TryTake(later));
        Assert.True(bucket.TryTake(later));
        Assert.False(bucket.TryTake(later));
    }
}
