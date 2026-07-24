using System.ComponentModel;
using System.Security.Claims;
using Chatbot.Domain.Tools;
using ModelContextProtocol.Server;

namespace Chatbot.Api;

/// <summary>
/// Ferramentas expostas via MCP (protocolo padrão de tool-calling): qualquer agente
/// MCP-compatível descobre e chama estas operações — sempre pelos MESMOS guardrails
/// do registro (RBAC do usuário logado + always_ask pra ação destrutiva). Só entra
/// aqui ferramenta com downstream real; stub não vira ferramenta de agente.
/// </summary>
[McpServerToolType]
public sealed class PlataformaTools(
    IHttpContextAccessor httpContext,
    IHttpClientFactory httpFactory,
    ToolGuardrails guardrails)
{
    [McpServerTool(Name = "consultar_ordem")]
    [Description("Consulta uma ordem de produção pelo id (estado, linha, produto, quantidade).")]
    public async Task<string> ConsultarOrdemAsync(
        [Description("Id (UUID) da ordem de produção")] Guid ordemId,
        CancellationToken ct)
    {
        Authorize("consultar_ordem", humanConfirmed: false);

        var response = await httpFactory.CreateClient("core-execution")
            .GetAsync(new Uri($"/v1/core/ordens/{ordemId}", UriKind.Relative), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return $"Ordem {ordemId} não encontrada.";
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [McpServerTool(Name = "abortar_ordem")]
    [Description("Aborta uma ordem de produção. Ação destrutiva: exige confirmação humana explícita.")]
    public async Task<string> AbortarOrdemAsync(
        [Description("Id (UUID) da ordem de produção")] Guid ordemId,
        [Description("true somente se um humano confirmou explicitamente esta ação")] bool humanoConfirmou,
        CancellationToken ct)
    {
        Authorize("abortar_ordem", humanoConfirmou);

        var response = await httpFactory.CreateClient("core-execution")
            .PostAsync(new Uri($"/v1/core/ordens/{ordemId}/abortar", UriKind.Relative), content: null, ct);
        response.EnsureSuccessStatusCode();
        return $"Ordem {ordemId} abortada.";
    }

    [McpServerTool(Name = "buscar_conhecimento")]
    [Description("Busca semântica na base de conhecimento (manuais, runbooks, políticas), já filtrada pelo RBAC do usuário.")]
    public async Task<string> BuscarConhecimentoAsync(
        [Description("Pergunta ou termos de busca")] string consulta,
        CancellationToken ct)
    {
        Authorize("buscar_conhecimento", humanConfirmed: false);

        // Knowledge exige JWT: repassa o token do usuário — a busca lá dentro filtra
        // visibilidade pelos papéis DELE, não por um papel de serviço.
        var client = httpFactory.CreateClient("knowledge");
        var bearer = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri("/v1/knowledge/graphql", UriKind.Relative));
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
        request.Content = JsonContent.Create(new
        {
            query = "query($q:String!){ search(query:$q, limit:5){ title content score } }",
            variables = new { q = consulta },
        });

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [McpServerTool(Name = "consultar_causa_raiz")]
    [Description("Diagnóstico Ishikawa 6M: dado um sintoma (parada, defeito), devolve as causas raiz mais prováveis da base de conhecimento, com categoria, confiança e score de similaridade.")]
    public async Task<string> ConsultarCausaRaizAsync(
        [Description("Sintoma observado (ex.: 'ponte de solda recorrente na linha 2')")] string sintoma,
        [Description("Id do ativo/linha pra restringir (opcional; vazio busca em todos)")] string? ativoId,
        CancellationToken ct)
    {
        Authorize("consultar_causa_raiz", humanConfirmed: false);

        var client = httpFactory.CreateClient("knowledge");
        var bearer = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri("/v1/knowledge/graphql", UriKind.Relative));
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
        request.Content = JsonContent.Create(new
        {
            query = """
                query($s:String!, $a:String) {
                  diagnosticoPorSintoma(sintoma:$s, ativoId:$a, limit:5) {
                    score
                    causa { ativoId categoria sintoma causa motivoCodigo confianca }
                  }
                }
                """,
            variables = new { s = sintoma, a = string.IsNullOrWhiteSpace(ativoId) ? null : ativoId },
        });

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [McpServerTool(Name = "diagnosticar_parada")]
    [Description("iDMSS: 'por que a linha parou?' — ranking explicável dos sintomas MES recentes do ativo, enriquecido com as causas raiz Ishikawa da base de conhecimento.")]
    public async Task<string> DiagnosticarParadaAsync(
        [Description("Id do ativo/linha (ex.: 'envase.spitau.linha2.enchedora')")] string ativoId,
        CancellationToken ct)
    {
        Authorize("diagnosticar_parada", humanConfirmed: false);

        var client = httpFactory.CreateClient("agents");
        var bearer = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"/v1/agents/idmss/diagnose?ativoId={Uri.EscapeDataString(ativoId)}", UriKind.Relative));
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", bearer);

        var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return $"Sem eventos MES recentes para {ativoId} — nada a diagnosticar na janela.";
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [McpServerTool(Name = "avaliar_regras_ishikawa")]
    [Description("Inferência Lógica Ishikawa 6M (Épico #10): avalia um conjunto de fatos observados na linha SMT contra as regras de causa raiz e devolve as hipóteses deduzidas com explicação auditável.")]
    public async Task<string> AvaliarRegrasIshikawaAsync(
        [Description("Lista de fatos observados (ex.: 'SPI_Volume_Baixo', 'SOLDER-BRIDGE-RISK')")] IReadOnlyList<string> fatos,
        CancellationToken ct)
    {
        Authorize("avaliar_regras_ishikawa", humanConfirmed: false);

        var client = httpFactory.CreateClient("knowledge");
        var bearer = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri("/v1/knowledge/graphql", UriKind.Relative));
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.TryAddWithoutValidation("Authorization", bearer);
        request.Content = JsonContent.Create(new
        {
            query = """
                query($f:[String!]!) {
                  avaliarRegrasIshikawa(fatosObservados:$f) {
                    ruleId categoria causaRaiz acaoRecomendada confiancaFinal fatosSatisfeitos racionalExplicativo
                  }
                }
                """,
            variables = new { f = fatos },
        });

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // O agente nunca passa por cima do guardrail: negação vira exceção MCP visível no trace.
    private void Authorize(string tool, bool humanConfirmed)
    {
        var user = httpContext.HttpContext?.User
            ?? throw new InvalidOperationException("Sem contexto HTTP — MCP fora do endpoint autenticado");
        var subject = SubjectFrom(user);

        var access = guardrails.Evaluate(tool, subject, humanConfirmed);
        if (access != ToolAccess.Allowed)
        {
            throw new UnauthorizedAccessException(access switch
            {
                ToolAccess.NeedsHumanConfirmation =>
                    "Ação destrutiva exige confirmação humana (always_ask): repita com humanoConfirmou=true SOMENTE após um humano aprovar.",
                ToolAccess.DeniedByRbac => "Fora do RBAC do usuário logado.",
                _ => "Ferramenta não registrada.",
            });
        }
    }

    internal static Platform.AccessControl.Subject SubjectFrom(ClaimsPrincipal user) => new(
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
        user.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        user.Claims.Where(c => c.Type.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.Type[5..], c => c.Value, StringComparer.OrdinalIgnoreCase));
}
