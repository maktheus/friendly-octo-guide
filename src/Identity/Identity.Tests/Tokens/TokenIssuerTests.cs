using Identity.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Tests.Tokens;

public class TokenIssuerTests
{
    [Fact]
    public void Sem_chave_configurada_o_emissor_nem_constroi()
    {
        // O host resolve a chave no boot (PlatformSecrets.JwtSigningKey); se ela não
        // chegou aqui, algo removeu essa etapa — melhor falhar do que assinar com default.
        Assert.Throws<InvalidOperationException>(
            () => new TokenIssuer(new ConfigurationBuilder().Build()));
    }
}
