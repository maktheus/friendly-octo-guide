using Chatbot.Domain.Rag;
using Chatbot.Domain.Tools;
using Platform.AccessControl;

namespace Chatbot.Api;

/// <summary>
/// Registro de ferramentas expostas ao agente — espelha as operações dos MCP servers
/// dos serviços .NET (fase 1: descoberta dinâmica via protocolo MCP).
/// </summary>
public static class ToolRegistry
{
    public static ToolGuardrails Default() => new(
        new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["consultar_ordem"] = new("consultar_ordem", ToolKind.Read,
                RouteRequirement.ForRoles("operador", "admin")),
            ["consultar_telemetria"] = new("consultar_telemetria", ToolKind.Read,
                RouteRequirement.ForRoles("operador", "admin")),
            ["buscar_conhecimento"] = new("buscar_conhecimento", ToolKind.Read,
                RouteRequirement.ForRoles("operador", "admin")),
            ["consultar_causa_raiz"] = new("consultar_causa_raiz", ToolKind.Read,
                RouteRequirement.ForRoles("operador", "admin")),
            ["diagnosticar_parada"] = new("diagnosticar_parada", ToolKind.Read,
                RouteRequirement.ForRoles("operador", "admin")),
            ["abortar_ordem"] = new("abortar_ordem", ToolKind.DestructiveAct,
                RouteRequirement.ForRoles("operador", "admin")),
            ["propor_comando_linha"] = new("propor_comando_linha", ToolKind.DestructiveAct,
                RouteRequirement.ForRoles("admin")),
        });
}

/// <summary>Corpus inicial do RAG — fase 1: pgvector + embeddings workers do 3b.</summary>
public static class RagCorpus
{
    public static IReadOnlyList<RagDocument> Seed()
    {
        var operacao = RouteRequirement.ForRoles("operador", "admin");
        var gestao = RouteRequirement.ForRoles("admin");

        return
        [
            new("manual-forno", "Manual do forno 01: faixa de operação -40 a 900 graus, "
                + "parada obrigatória acima de 850 por mais de 5 minutos.", operacao),
            new("runbook-linha-2", "Runbook da linha 2: parada por temperatura alta exige "
                + "inspeção do termopar e verificação da válvula antes de religar.", operacao),
            new("politica-turnos", "Política de turnos: madrugada opera com equipe reduzida; "
                + "comandos de criticidade média sobem pra aprovação do gestor de plantão.", gestao),
        ];
    }
}
