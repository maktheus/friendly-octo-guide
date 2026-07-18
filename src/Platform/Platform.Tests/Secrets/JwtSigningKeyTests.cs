using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults;
using Xunit;

namespace Platform.Tests.Secrets;

public class JwtSigningKeyTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? key) => new ConfigurationBuilder()
        .AddInMemoryCollection(key is null
            ? []
            : new Dictionary<string, string?> { ["Jwt:SigningKey"] = key })
        .Build();

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Chave_configurada_vence_em_qualquer_ambiente(string env)
    {
        var key = PlatformSecrets.JwtSigningKey(Config("chave-do-openbao-32-bytes-ok!!!!"), new FakeEnvironment(env));
        Assert.Equal("chave-do-openbao-32-bytes-ok!!!!", key);
    }

    [Fact]
    public void Sem_chave_em_Development_cai_no_fallback_de_dev()
    {
        var key = PlatformSecrets.JwtSigningKey(Config(null), new FakeEnvironment("Development"));
        Assert.Equal(PlatformSecrets.DevJwtSigningKey, key);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Sem_chave_fora_de_Development_o_boot_falha(string env)
    {
        // A chave de dev está num repo público: assinar com ela fora de dev
        // é equivalente a não assinar. Falhar cedo é o comportamento seguro.
        Assert.Throws<InvalidOperationException>(
            () => PlatformSecrets.JwtSigningKey(Config(null), new FakeEnvironment(env)));
    }

    [Fact]
    public void Chave_vazia_conta_como_ausente()
    {
        Assert.Throws<InvalidOperationException>(
            () => PlatformSecrets.JwtSigningKey(Config(""), new FakeEnvironment("Production")));
    }
}
