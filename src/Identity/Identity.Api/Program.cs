using Identity.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.Audit;
using Platform.ServiceDefaults;

var instrumentation = new ServiceInstrumentation("identity");

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults(instrumentation);

// OpenBao é a fonte da chave de assinatura em prod; sem ele configurado (dev local),
// cai no valor de Jwt:SigningKey do appsettings.
if (await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/jwt", "signingKey") is { } jwtKey)
    builder.Configuration["Jwt:SigningKey"] = jwtKey;
// Resolve a chave UMA vez: fallback de dev só em Development; sem chave fora disso, o boot falha.
builder.Configuration["Jwt:SigningKey"] = PlatformSecrets.JwtSigningKey(builder.Configuration, builder.Environment);

// As rotas admin validam o PRÓPRIO token que o Identity emite (defesa em profundidade:
// em dev local o serviço fica exposto sem o Gateway na frente — era exatamente por aqui
// que o endpoint admin ficava aberto, com ator fixo "system:dev" na auditoria).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // claims cruas ("sub", "role"), como o TokenIssuer emite
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "identity",
            ValidAudience = "plataforma-linha",
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
            NameClaimType = "sub",   // ator da auditoria = usuário do token
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization(o => o.AddPolicy("admin", p => p.RequireRole("admin")));

// Keycloak:BaseUrl configurado → ele é a fonte da verdade de usuários/senha/TOTP;
// o client secret vem do OpenBao (nunca do compose em texto puro).
var keycloakBaseUrl = builder.Configuration["Keycloak:BaseUrl"];
KeycloakAuthClient? keycloakClient = null;
if (!string.IsNullOrEmpty(keycloakBaseUrl))
{
    var clientId = await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/keycloak", "clientId") ?? "identity-api";
    var clientSecret = await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/keycloak", "clientSecret");
    if (clientSecret is not null)
    {
        var realm = builder.Configuration["Keycloak:Realm"] ?? "plataforma-linha";
        keycloakClient = new KeycloakAuthClient(new HttpClient { BaseAddress = new Uri(keycloakBaseUrl) }, realm, clientId, clientSecret);
    }
}

builder.Services.AddSingleton(instrumentation);
// Uma instância do store atende leitura (IUserStore) e admin (IUserAdmin) — sem
// Keycloak (dev local), os usuários seguem em memória.
builder.Services.AddSingleton<InMemoryUserStore>();
builder.Services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<InMemoryUserStore>());
builder.Services.AddSingleton<IUserAdmin>(sp => sp.GetRequiredService<InMemoryUserStore>());
// Trilha de auditoria: com Postgres configurado, grava durável no outbox e um
// relay leva ao Kafka (auditoria.admin.v1) → lake WORM (fase 1). Sem Postgres
// (dev local puro), cai no sink de log (fase 0). A interface não muda.
var auditConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrEmpty(auditConnectionString))
{
    builder.Services.AddSingleton(new AdminAuditStore(auditConnectionString));
    builder.Services.AddSingleton<IAdminAuditTrail, PostgresAdminAuditTrail>();
    builder.Services.AddHostedService<AdminAuditOutboxRelay>();
}
else
{
    builder.Services.AddSingleton<IAdminAuditTrail, LoggingAdminAuditTrail>();
}
builder.Services.AddSingleton<TokenIssuer>();
if (keycloakClient is not null)
    builder.Services.AddSingleton(keycloakClient);
builder.Services.AddSingleton(sp => new LoginFlow(
    sp.GetRequiredService<IUserStore>(),
    sp.GetRequiredService<TokenIssuer>(),
    keycloakClient));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapAdminEndpoints();

app.Run();
