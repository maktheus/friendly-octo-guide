using System.Globalization;
using Knowledge.Domain.Ishikawa;

namespace Knowledge.Domain.Rules;

/// <summary>
/// Motor de inferência dedutiva Ishikawa 6M (Épico #10 - Jeymerson).
/// Avalia conjuntos de fatos em tempo real da linha SMT contra o repositório de regras
/// elicitadas da operação, gerando diagnósticos categorizados nas 6M com explicabilidade total.
/// </summary>
public static class IshikawaRuleEngine
{
    /// <summary>
    /// Avalia fatos observados da fábrica contra o banco de regras Ishikawa.
    /// Retorna diagnósticos cujas condições foram plenamente satisfeitas.
    /// </summary>
    public static IReadOnlyList<DiagnosticoIshikawa> Avaliar(
        IEnumerable<string> fatosObservados,
        IEnumerable<IshikawaRule> regras)
    {
        ArgumentNullException.ThrowIfNull(fatosObservados);
        ArgumentNullException.ThrowIfNull(regras);

        var fatosSet = fatosObservados
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resultados = new List<DiagnosticoIshikawa>();

        foreach (var regra in regras)
        {
            var fatosSatisfeitos = regra.CondicoesFatos
                .Where(cond => fatosSet.Contains(cond.Trim()))
                .ToList();

            // Verifica se todas as condições da regra foram atendidas
            if (fatosSatisfeitos.Count == regra.CondicoesFatos.Count && regra.CondicoesFatos.Count > 0)
            {
                var explicacao = string.Create(CultureInfo.InvariantCulture,
                    $"Regra [{regra.RuleId}] '{regra.Nome}' disparada por fatos: ({string.Join(", ", fatosSatisfeitos)}). Categoria 6M: {regra.Categoria}. Racional: {regra.Racional}");

                resultados.Add(new DiagnosticoIshikawa(
                    regra.RuleId,
                    regra.Categoria,
                    regra.CausaRaiz,
                    regra.AcaoRecomendada,
                    regra.Confianca,
                    fatosSatisfeitos,
                    explicacao
                ));
            }
        }

        return [.. resultados.OrderByDescending(r => r.ConfiancaFinal)];
    }

    /// <summary>
    /// Conjunto inicial de regras elicitadas para linhas SMT (Solder Paste, Reflow, Pick & Place).
    /// </summary>
    public static IReadOnlyList<IshikawaRule> RegrasPadrao { get; } =
    [
        new(
            "R-SMT-001",
            "Insuficiência de Pasta de Solda por Obstrução",
            ["SPI_Volume_Baixo", "SOLDER-BRIDGE-RISK"],
            IshikawaCategory.Material,
            "Pasta de solda fora da especificação de viscosidade ou estêncil obstruído",
            "Realizar limpeza automática do estêncil e auditar viscosidade da pasta de solda",
            0.88,
            "SPI reportou volume baixo e risco de ponte de solda em área de pitch fino."
        ),
        new(
            "R-SMT-002",
            "Efeito Tombstone por Desbalanceamento Térmico",
            ["TOMBSTONE", "Reflow_Gradiente_Elevado"],
            IshikawaCategory.Maquina,
            "Perfil térmico desbalanceado na zona de pré-aquecimento do forno de reflow",
            "Re-perfilar zonas 3 e 4 do forno de reflow com placa de teste instrumentada",
            0.92,
            "Diferencial térmico entre os pads superou o limite durante o estagio de fusão."
        ),
        new(
            "R-SMT-003",
            "Desalinhamento Mecânico Pick & Place",
            ["PnP_Offset_Elevado", "AOI_Component_Misaligned"],
            IshikawaCategory.Maquina,
            "Desalinhamento mecânico do cabeçote ou desgaste do bocal da máquina Pick & Place",
            "Executar rotina de calibração de visão da PnP e substituir bocal #4",
            0.85,
            "Desvio sistemático no eixo X e Y reportado pela AOI após inserção."
        ),
        new(
            "R-SMT-004",
            "Falha de Operação no Manuseio de Componente Sensitive (MSD)",
            ["Componente_Umidificado", "Reflow_Popcorn_Defect"],
            IshikawaCategory.MaoDeObra,
            "Componente eletrônico sensível à umidade (MSD) exposto além do tempo limite",
            "Descartar lote afetado e reforçar procedimento de selagem a vácuo (J-STD-033)",
            0.90,
            "Tempo de exposição do componente ao ambiente superou o limite recomendado."
        )
    ];
}
