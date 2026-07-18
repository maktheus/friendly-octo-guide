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
builder.Services.AddHostedService<DailyReportService>();

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
