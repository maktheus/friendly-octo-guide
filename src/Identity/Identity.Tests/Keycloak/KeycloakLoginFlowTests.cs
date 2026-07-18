using System.Net;
using System.Text;
using Identity.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Tests.Keycloak;

/// <summary>
/// O fluxo delegado ao Keycloak, testado sem Keycloak: um handler fake devolve
/// as respostas reais do token endpoint (200 com tokens / 400 invalid_grant).
/// O que está em jogo é o CONTRATO do Identity: senha certa nunca emite token
/// sozinha, nem quando é o Keycloak validando.
/// </summary>
public class KeycloakLoginFlowTests
{
    private sealed class FakeKeycloakHandler(Func<IDictionary<string, string>, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var form = (await request.Content!.ReadAsStringAsync(ct))
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
            return respond(form);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage MissingTotp() =>
        Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid user credentials: missing OTP"}""");

    private static HttpResponseMessage WrongPassword() =>
        Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}""");

    private static HttpResponseMessage Tokens() =>
        Json(HttpStatusCode.OK, """{"access_token":"at-kc","refresh_token":"rt-kc"}""");

    private static KeycloakAuthClient Client(Func<IDictionary<string, string>, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeKeycloakHandler(respond)) { BaseAddress = new Uri("http://keycloak.test") },
            realm: "plataforma-linha", clientId: "identity", clientSecret: "s3cr3t");

    private static LoginFlow Flow(Func<IDictionary<string, string>, HttpResponseMessage> respond) =>
        new(new InMemoryUserStore(), new TokenIssuer(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Platform.ServiceDefaults.PlatformSecrets.DevJwtSigningKey,
            })
            .Build()), Client(respond));

    [Fact]
    public async Task Grant_400_pedindo_otp_vira_TotpRequired()
    {
        var result = await Client(_ => MissingTotp()).PasswordGrantAsync("op", "senha", totp: null);
        Assert.Equal(KeycloakGrantOutcome.TotpRequired, result.Outcome);
    }

    [Fact]
    public async Task Grant_400_por_credencial_errada_vira_Denied()
    {
        var result = await Client(_ => WrongPassword()).PasswordGrantAsync("op", "senha-errada", totp: null);
        Assert.Equal(KeycloakGrantOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task Grant_200_vira_Success_com_os_tokens_do_keycloak()
    {
        var result = await Client(_ => Tokens()).PasswordGrantAsync("op", "senha", "123456");
        Assert.Equal(KeycloakGrantOutcome.Success, result.Outcome);
        Assert.Equal("at-kc", result.AccessToken);
        Assert.Equal("rt-kc", result.RefreshToken);
    }

    [Fact]
    public async Task Senha_certa_abre_desafio_quando_o_realm_exige_totp()
    {
        var challenge = await Flow(_ => MissingTotp()).BeginLogin("op", "senha");
        Assert.NotNull(challenge);
    }

    [Fact]
    public async Task Login_que_passaria_sem_segundo_fator_e_recusado()
    {
        // Usuário sem TOTP configurado no realm logaria só com senha — o Identity
        // recusa: 2FA é invariante da plataforma, não opção do usuário.
        var challenge = await Flow(_ => Tokens()).BeginLogin("op", "senha");
        Assert.Null(challenge);
    }

    [Fact]
    public async Task Desafio_completado_com_totp_emite_os_tokens_assinados_pelo_keycloak()
    {
        var flow = Flow(form => form.ContainsKey("totp") ? Tokens() : MissingTotp());

        var challenge = await flow.BeginLogin("op", "senha");
        var session = await flow.CompleteTotp(challenge!.Value, "123456");

        Assert.Equal(("at-kc", "rt-kc"), session);
    }

    [Fact]
    public async Task Desafio_e_de_uso_unico()
    {
        var flow = Flow(form => form.ContainsKey("totp") ? Tokens() : MissingTotp());
        var challenge = await flow.BeginLogin("op", "senha");

        Assert.NotNull(await flow.CompleteTotp(challenge!.Value, "123456"));
        Assert.Null(await flow.CompleteTotp(challenge.Value, "123456")); // replay do desafio
    }

    [Fact]
    public async Task Totp_errado_na_etapa_2_nao_emite_sessao()
    {
        var flow = Flow(form => form.ContainsKey("totp") ? WrongPassword() : MissingTotp());
        var challenge = await flow.BeginLogin("op", "senha");

        Assert.Null(await flow.CompleteTotp(challenge!.Value, "000000"));
    }
}
