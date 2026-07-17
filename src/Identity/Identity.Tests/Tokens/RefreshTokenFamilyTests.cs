using Identity.Domain.Tokens;
using Xunit;

namespace Identity.Tests.Tokens;

public class RefreshTokenFamilyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private readonly RefreshTokenFamily _family = new(tokenLifetime: TimeSpan.FromDays(7));

    [Fact]
    public void Token_valido_rotaciona_e_emite_um_novo_da_mesma_familia()
    {
        var first = _family.Issue(Guid.NewGuid(), Now);
        var result = _family.Redeem(first.Id, Now + TimeSpan.FromHours(1));

        Assert.Equal(RedeemOutcome.Rotated, result.Outcome);
        Assert.NotNull(result.NewToken);
        Assert.Equal(first.FamilyId, result.NewToken.FamilyId);
        Assert.NotEqual(first.Id, result.NewToken.Id);
    }

    [Fact]
    public void Token_expirado_e_recusado_sem_revogar_a_familia()
    {
        var token = _family.Issue(Guid.NewGuid(), Now);
        var result = _family.Redeem(token.Id, Now + TimeSpan.FromDays(8));

        Assert.Equal(RedeemOutcome.Expired, result.Outcome);
        Assert.False(result.RevokeFamily);
    }

    [Fact]
    public void Reuso_de_token_consumido_revoga_a_familia_inteira()
    {
        // Cenário de roubo: atacante e usuário legítimo têm o mesmo refresh token.
        var familyId = Guid.NewGuid();
        var stolen = _family.Issue(familyId, Now);

        var legit = _family.Redeem(stolen.Id, Now + TimeSpan.FromHours(1));   // usuário usa
        var attack = _family.Redeem(stolen.Id, Now + TimeSpan.FromHours(2));  // atacante reapresenta

        Assert.Equal(RedeemOutcome.ReuseDetected, attack.Outcome);
        Assert.True(attack.RevokeFamily);

        // O token que o usuário legítimo recebeu na rotação também morreu:
        var afterRevoke = _family.Redeem(legit.NewToken!.Id, Now + TimeSpan.FromHours(3));
        Assert.Equal(RedeemOutcome.Unknown, afterRevoke.Outcome);
    }

    [Fact]
    public void Token_desconhecido_e_recusado()
    {
        var result = _family.Redeem(Guid.NewGuid(), Now);
        Assert.Equal(RedeemOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public void Token_expirado_apresentado_e_removido_do_estado()
    {
        var token = _family.Issue(Guid.NewGuid(), Now);
        _family.Redeem(token.Id, Now + TimeSpan.FromDays(8));       // Expired: sai do dicionário
        var again = _family.Redeem(token.Id, Now + TimeSpan.FromDays(8));

        Assert.Equal(RedeemOutcome.Unknown, again.Outcome);
    }

    [Fact]
    public void Prune_devolve_familias_que_ficaram_sem_token_vivo()
    {
        var deadFamily = Guid.NewGuid();
        var liveFamily = Guid.NewGuid();
        _family.Issue(deadFamily, Now);
        _family.Issue(liveFamily, Now + TimeSpan.FromDays(5)); // ainda vale no corte

        var pruned = _family.PruneExpired(Now + TimeSpan.FromDays(8));

        Assert.Equal(deadFamily, Assert.Single(pruned));
    }

    [Fact]
    public void Reuso_informa_a_familia_revogada()
    {
        var familyId = Guid.NewGuid();
        var stolen = _family.Issue(familyId, Now);
        _family.Redeem(stolen.Id, Now + TimeSpan.FromHours(1));
        var attack = _family.Redeem(stolen.Id, Now + TimeSpan.FromHours(2));

        Assert.Equal(familyId, attack.FamilyId); // quem guarda estado por família limpa por aqui
    }
}
