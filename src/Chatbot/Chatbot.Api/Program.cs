using System.Security.Claims;
using Chatbot.Api;
using Chatbot.Domain.Rag;
using Chatbot.Domain.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.AccessControl;
using Platform.ServiceDefaults;

// Chatbot + agentes (5b): LLM open-weight auto-hospedado (vLLM) com RAG que respeita
// o RBAC do usuário logado. Ação = ferramenta tipada com guardrail always_ask.
// Toda conversa e chamada de ferramenta vira trace na espinha de observabilidade.

var instrumentation = new ServiceInstrumentation("chatbot");

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults(instrumentation);

// Defesa em profundidade: o Gateway já validou, mas este serviço revalida o JWT —
// um pod exposto por engano não vira bypass.
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
builder.Services.AddHttpClient<VllmChat>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Vllm:BaseUrl"] ?? "http://localhost:8000");
    c.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton(ToolRegistry.Default());
builder.Services.AddSingleton(RagCorpus.Seed());

// Ferramentas via MCP: agentes descobrem e chamam pelo protocolo padrão, e cada
// chamada passa pelos mesmos guardrails (RBAC + always_ask) do endpoint REST.
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("core-execution", c =>
    c.BaseAddress = new Uri(builder.Configuration["CoreExecution:BaseUrl"] ?? "http://core-execution:8080"));
builder.Services.AddHttpClient("knowledge", c =>
    c.BaseAddress = new Uri(builder.Configuration["Knowledge:BaseUrl"] ?? "http://knowledge:8080"));
builder.Services.AddHttpClient("agents", c =>
    c.BaseAddress = new Uri(builder.Configuration["Agents:BaseUrl"] ?? "http://agents:8080"));
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<PlataformaTools>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/chat", async (
    ChatRequest req, VllmChat vllm, IReadOnlyList<RagDocument> corpus, HttpContext ctx, CancellationToken ct) =>
{
    var subject = SubjectFrom(ctx.User);
    using var activity = instrumentation.Activity.StartActivity("chat.turn");
    activity?.SetTag("chat.user", subject.UserId);

    var context = RagContextBuilder.Select(corpus, subject, req.Message, maxDocuments: 3);
    var answer = await vllm.AskAsync(req.Message, context, ct);

    return Results.Ok(new
    {
        answer,
        sources = context.Select(d => d.Id),
        traceId = activity?.TraceId.ToString(),
    });
}).RequireAuthorization();

app.MapPost("/v1/chat/ferramentas/{name}", (
    string name, ToolCallRequest req, ToolGuardrails guardrails, HttpContext ctx) =>
{
    var subject = SubjectFrom(ctx.User);
    using var activity = instrumentation.Activity.StartActivity("chat.tool");
    activity?.SetTag("tool.name", name);
    activity?.SetTag("chat.user", subject.UserId);

    var access = guardrails.Evaluate(name, subject, req.HumanConfirmed);
    activity?.SetTag("tool.access", access.ToString());

    return access switch
    {
        // A execução real sai pelo MCP server do serviço dono da operação (fase 1);
        // aqui devolvemos o veredito do guardrail — que é o contrato auditado.
        ToolAccess.Allowed => Results.Ok(new { status = "executar", tool = name }),
        ToolAccess.NeedsHumanConfirmation => Results.Json(
            new { status = "confirmar", detail = "Ação destrutiva exige confirmação humana (always_ask)." },
            statusCode: StatusCodes.Status409Conflict),
        ToolAccess.DeniedByRbac => Results.Json(
            new { status = "negado", detail = "Fora do RBAC do usuário logado." },
            statusCode: StatusCodes.Status403Forbidden),
        _ => Results.NotFound(new { status = "desconhecida" }),
    };
}).RequireAuthorization();

app.MapMcp("/v1/chat/mcp").RequireAuthorization();

app.Run();

static Subject SubjectFrom(ClaimsPrincipal user) => new(
    user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
    user.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase),
    user.Claims.Where(c => c.Type.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(c => c.Type[5..], c => c.Value, StringComparer.OrdinalIgnoreCase));

namespace Chatbot.Api
{
    public sealed record ChatRequest(string Message);
    public sealed record ToolCallRequest(bool HumanConfirmed);
}
