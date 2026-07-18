using Agents.Api;
using Agents.Domain.Actions;
using Agents.Domain.Diagnosis;
using Agents.Domain.Reporting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.ServiceDefaults;

// Agentes de operação (5b): diagnóstico de incidente correlacionando sinais da
// espinha de observabilidade, abertura de ticket e relatório diário agendado.
// Toda ação passa pelo guardrail — ler/reportar é livre, mexer na linha vai pro
// Decision Engine. Toda diagnose vira trace.

var instrumentation = new ServiceInstrumentation("agents");

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults(instrumentation);

if (await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/jwt", "signingKey") is { } jwtKey)
    builder.Configuration["Jwt:SigningKey"] = jwtKey;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = "identity",
        ValidAudience = "plataforma-linha",
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
            PlatformSecrets.JwtSigningKey(builder.Configuration, builder.Environment))),
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton(instrumentation);
builder.Services.AddSingleton(new SignalWindow(
    TimeSpan.FromHours(builder.Configuration.GetValue("Agents:WindowHours", 24))));
builder.Services.AddHostedService<AlertIngestService>();
builder.Services.AddHostedService<MesIngestService>(); // alimenta o iDMSS (épico #8)
builder.Services.AddHostedService<DailyReportService>();

// O iDMSS enriquece o ranking com a base de causa raiz do Knowledge (épico #7).
builder.Services.AddHttpClient("knowledge", c =>
    c.BaseAddress = new Uri(builder.Configuration["Knowledge:BaseUrl"] ?? "http://knowledge:8080"));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// Diagnóstico sob demanda: correlaciona a janela quente num palpite de causa raiz.
app.MapGet("/v1/agents/diagnose", (SignalWindow window) =>
{
    using var activity = instrumentation.Activity.StartActivity("agents.diagnose");
    var diagnosis = IncidentDiagnoser.Diagnose(window.Snapshot(), TimeSpan.FromMinutes(30));
    activity?.SetTag("diagnosis.found", diagnosis is not null);
    return diagnosis is null ? Results.NoContent() : Results.Ok(diagnosis);
}).RequireAuthorization();

// iDMSS (épico #8): "por que a linha parou?" — ranking explicável dos sintomas MES
// da janela (frequência × peso do modelo; RF pluga no peso) enriquecido com a base
// de causa raiz Ishikawa do Knowledge. Interface de decisão, nunca de execução:
// ação física segue pelo /propor-acao → Decision Engine.
app.MapGet("/v1/agents/idmss/diagnose", async (
    string ativoId, int? top, SignalWindow window,
    IHttpClientFactory http, HttpContext ctx, CancellationToken ct) =>
{
    using var activity = instrumentation.Activity.StartActivity("agents.idmss.diagnose");
    var result = Agents.Domain.Idmss.IdmssDiagnosis.Rank(
        window.Snapshot(), ativoId, Math.Clamp(top ?? 3, 1, 10));
    activity?.SetTag("idmss.ocorrencias", result.Ocorrencias);

    if (result.Ranking.Count == 0)
        return Results.NoContent();

    // Enriquecimento Ishikawa: busca semântica pro sintoma do topo, com o token do
    // usuário (a visibilidade é DELE). Knowledge fora do ar não cala o ranking.
    System.Text.Json.JsonElement? causasProvaveis = null;
    try
    {
        var client = http.CreateClient("knowledge");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri("/v1/knowledge/graphql", UriKind.Relative));
        var bearer = ctx.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
        request.Content = JsonContent.Create(new
        {
            query = """
                query($s:String!, $a:String) {
                  diagnosticoPorSintoma(sintoma:$s, ativoId:$a, limit:3) {
                    score
                    causa { categoria sintoma causa confianca }
                  }
                }
                """,
            variables = new { s = result.Ranking[0].Sintoma, a = (string?)null },
        });
        var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            causasProvaveis = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        }
    }
    catch (HttpRequestException) { /* ranking responde mesmo sem a base */ }

    return Results.Ok(new
    {
        result.AtivoId,
        result.Ocorrencias,
        ranking = result.Ranking,
        causasProvaveis,
        proximoPasso = "Validar a causa apontada com a operação; confirmada, registrar via "
            + "registrarCausaRaiz (Knowledge). Ação física NUNCA sai daqui: /v1/agents/propor-acao → Decision Engine.",
    });
}).RequireAuthorization();

// Relatório do dia sob demanda (o agendado publica no Kafka; este é pra consulta).
app.MapGet("/v1/agents/report/today", (SignalWindow window) =>
    Results.Ok(DailyReportBuilder.Build(window.Snapshot(), DateOnly.FromDateTime(DateTime.UtcNow))))
    .RequireAuthorization();

// Propõe uma ação corretiva: o guardrail decide se segue, pede humano ou vai pro
// Decision Engine. O agente NUNCA executa direto por este endpoint.
app.MapPost("/v1/agents/propor-acao", (ProporAcaoRequest req) =>
{
    var verdict = AgentActionPolicy.Evaluate(req.Kind, req.HumanConfirmed);
    return verdict switch
    {
        ActionVerdict.Proceed => Results.Ok(new { status = "executar", req.Kind }),
        ActionVerdict.NeedsHumanConfirmation => Results.Json(
            new { status = "confirmar", detail = "Ação corretiva exige confirmação humana (always_ask)." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(
            new { status = "decision-engine", detail = "Ação física roteada pro loop auditado do Decision Engine." },
            statusCode: StatusCodes.Status202Accepted),
    };
}).RequireAuthorization();

app.Run();

namespace Agents.Api
{
    public sealed record ProporAcaoRequest(ActionKind Kind, bool HumanConfirmed);
}
