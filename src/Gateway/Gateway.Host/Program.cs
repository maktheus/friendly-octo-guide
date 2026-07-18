using System.Security.Claims;
using Gateway.Domain;
using Gateway.Host;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.AccessControl;
using Platform.ServiceDefaults;
using StackExchange.Redis;

// Ponto único de entrada: valida o JWT emitido pelo Identity (ou, quando o Keycloak
// está configurado, o JWT que o Identity repassa do Keycloak), aplica RBAC/ABAC por
// rota, limita taxa por usuário e propaga o traceparent (W3C) — o trace-id raiz nasce aqui.

var instrumentation = new ServiceInstrumentation("gateway");

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults(instrumentation);

if (await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/jwt", "signingKey") is { } jwtKey)
    builder.Configuration["Jwt:SigningKey"] = jwtKey;

// Keycloak configurado (Keycloak:BaseUrl) → valida via JWKS do realm (assinatura assimétrica,
// Keycloak é a fonte da verdade). Sem ele → chave simétrica compartilhada com o Identity local.
var keycloakBaseUrl = builder.Configuration["Keycloak:BaseUrl"];
var keycloakRealm = builder.Configuration["Keycloak:Realm"] ?? "plataforma-linha";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Preserva os nomes de claim como o Identity emite ("sub", "role", "attr:*") —
        // sem o remapeamento default (sub→nameidentifier, role→ClaimTypes.Role), que
        // faria o ClaimsMapper (que lê os nomes crus) não achar papel nenhum.
        o.MapInboundClaims = false;
        if (!string.IsNullOrEmpty(keycloakBaseUrl))
        {
            o.Authority = $"{keycloakBaseUrl}/realms/{keycloakRealm}";
            o.RequireHttpsMetadata = false; // rede interna do compose/cluster, sem TLS entre serviços ainda
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudience = "plataforma-linha",
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        }
        else
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "identity",
                ValidAudience = "plataforma-linha",
                IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
                    PlatformSecrets.JwtSigningKey(builder.Configuration, builder.Environment))),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        }
    });
builder.Services.AddAuthorization();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Rate limit: com Valkey configurado o limite vale no agregado entre réplicas;
// sem ele (dev, instância única) o limitador local por réplica basta.
var valkeyEndpoint = builder.Configuration.GetConnectionString("Valkey");
if (!string.IsNullOrEmpty(valkeyEndpoint))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect($"{valkeyEndpoint},abortConnect=false,connectRetry=5"));
    builder.Services.AddSingleton<IRateLimiter>(sp => new ValkeyRateLimiter(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        capacity: 20, refillPerSecond: 10,
        sp.GetRequiredService<ILogger<ValkeyRateLimiter>>(),
        instrumentation.Meter));
}
else
{
    builder.Services.AddSingleton<IRateLimiter>(new InMemoryRateLimiter(capacity: 20, refillPerSecond: 10));
}

// A tabela de rotas é a política de acesso da borda — versionada junto com o código.
builder.Services.AddSingleton(new RouteTable()
    .Public("/v1/auth")
    .Public("/healthz")
    .Public("/rum")
    .Require("/v1/core", RouteRequirement.ForRoles("operador", "admin"))
    .Require("/v1/core/admin", RouteRequirement.ForRoles("admin"))
    .Require("/v1/chat", RouteRequirement.ForRoles("operador", "admin"))
    .Require("/v1/knowledge", RouteRequirement.ForRoles("operador", "admin"))
    .Require("/v1/agents", RouteRequirement.ForRoles("operador", "admin"))
    .Require("/v1/linha", new RouteRequirement(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "operador", "admin" },
        new Dictionary<string, string>()))
    // Upgrade de WebSocket do painel: o browser NÃO envia Authorization no upgrade,
    // então a borda só encaminha; a credencial vai no PRIMEIRO frame e é o hub do
    // telemetry-ingest quem valida assinatura + papel antes de transmitir qualquer byte.
    .Public("/v1/linha/ws"));

// Deprecação de API versionada (RFC 8594): rota antiga anuncia Sunset ANTES de sumir.
// Sem entradas por padrão — preenchida quando uma v1 ganhar sucessora v2.
builder.Services.AddSingleton(new DeprecationTable());

var app = builder.Build();

// A plataforma suporta os DOIS modos: single-tenant por instância (default, ADR)
// e multi-tenant no mesmo cluster. Flag de config lida da app (não um local de
// startup — assim reflete override/reload e é testável via WebApplicationFactory).
var multiTenantEnabled = app.Configuration.GetValue("MultiTenant:Enabled", false);

app.UseAuthentication();

// Anuncia deprecação na resposta de rotas versionadas marcadas (Deprecation/Sunset/Link).
app.Use(async (ctx, next) =>
{
    var deprecation = ctx.RequestServices.GetRequiredService<DeprecationTable>().For(ctx.Request.Path);
    if (deprecation is not null)
    {
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers["Deprecation"] = deprecation.Deprecation;
            ctx.Response.Headers["Sunset"] = deprecation.Sunset;
            if (deprecation.Successor is { } successor)
                ctx.Response.Headers["Link"] = $"<{successor}>; rel=\"successor-version\"";
            return Task.CompletedTask;
        });
    }
    await next();
});

// Rate limit por usuário autenticado (ou IP, pré-login): 20 req de burst, 10 req/s de regime.
app.Use(async (ctx, next) =>
{
    var key = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? ctx.User.FindFirstValue("sub")
           ?? ctx.Connection.RemoteIpAddress?.ToString()
           ?? "anonymous";
    var limiter = ctx.RequestServices.GetRequiredService<IRateLimiter>();
    if (!await limiter.TryTakeAsync(key, DateTimeOffset.UtcNow, ctx.RequestAborted))
    {
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
    await next();
});

// AuthZ na borda: papel + atributos decidem a rota ANTES do proxy encaminhar.
app.Use(async (ctx, next) =>
{
    var table = ctx.RequestServices.GetRequiredService<RouteTable>();
    var match = table.Match(ctx.Request.Path);

    if (!match.IsListed)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound; // rota não listada nem existe pra fora
        return;
    }

    if (!match.IsPublic)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var subject = ClaimsMapper.ToSubject(
            ctx.User.FindFirstValue("sub") ?? "unknown",
            ctx.User.Claims.Select(c => (c.Type, c.Value)));

        var decision = AccessPolicy.Evaluate(subject, match.Requirement!);
        if (decision != AccessDecision.Allow)
        {
            instrumentation.Meter.CreateCounter<long>("gateway.authz.denied")
                .Add(1, new KeyValuePair<string, object?>("reason", decision.ToString()));
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Isolamento multi-tenant (quando MultiTenant:Enabled): o tenant do recurso
        // vem do header X-Tenant e tem que bater com o claim do usuário. Desligado
        // (default, single-tenant por instância), este bloco não interfere.
        if (multiTenantEnabled)
        {
            var resourceTenant = ctx.Request.Headers["X-Tenant"].ToString();
            if (string.IsNullOrEmpty(resourceTenant))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest; // multi-tenant exige o header
                return;
            }
            var tenantDecision = TenantPolicy.Evaluate(subject, resourceTenant, enabled: true);
            if (tenantDecision != TenantDecision.Allow)
            {
                instrumentation.Meter.CreateCounter<long>("gateway.tenant.denied")
                    .Add(1, new KeyValuePair<string, object?>("reason", tenantDecision.ToString()));
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
    }

    await next();
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// RUM do front: o PWA manda beacons anônimos de rota/status/duração via sendBeacon.
// Viram histograma OTel (client.rum.duration) — mesmo pipeline dos serviços.
var rumDuration = instrumentation.Meter.CreateHistogram<double>("client.rum.duration", unit: "ms");
app.MapPost("/rum", async (HttpRequest req) =>
{
    if (req.ContentLength is > RumBeacon.MaxPayloadBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    using var buffer = new MemoryStream();
    await req.Body.CopyToAsync(buffer, req.HttpContext.RequestAborted);
    if (!RumBeacon.TryParse(buffer.ToArray(), out var beacon))
        return Results.BadRequest();

    rumDuration.Record(beacon!.DurationMs,
        new KeyValuePair<string, object?>("route", beacon.Route),
        new KeyValuePair<string, object?>("status", beacon.Status));
    return Results.Accepted();
});

app.MapReverseProxy();

app.Run();

// Exposto pro teste de integração (WebApplicationFactory<Program>).
public partial class Program;
