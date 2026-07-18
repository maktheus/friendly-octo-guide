using System.Globalization;

namespace Knowledge.Domain.Inferencia;

/// <summary>Regra interpretável: SE o fato vale, ENTÃO o outro vale com esta confiança.</summary>
public sealed record Regra(string Se, string Entao, double Confianca, string Racional);

/// <summary>Conclusão derivada: o fato, a confiança acumulada e a CADEIA que a explica (XAI).</summary>
public sealed record Conclusao(string Fato, double Confianca, IReadOnlyList<string> Cadeia);

/// <summary>
/// Motor de inferência do épico #10 (proposta Jeymerson): encadeamento pra frente
/// (forward chaining) sobre regras causa→efeito interpretáveis — o complemento
/// SIMBÓLICO do RAG. A confiança se propaga por produto (cadeia longa = confiança
/// menor, como deve ser) e toda conclusão carrega a cadeia inteira que a gerou:
/// não existe "o sistema achou" sem o porquê. Puro e determinístico.
/// </summary>
public static class MotorInferencia
{
    /// <summary>
    /// Deriva tudo que as regras alcançam a partir dos fatos observados. Fato já
    /// concluído só é substituído por caminho de confiança MAIOR; ciclo não trava
    /// (profundidade limitada e produto de confiança só decresce).
    /// </summary>
    public static IReadOnlyList<Conclusao> Encadear(
        IEnumerable<string> fatosObservados, IReadOnlyList<Regra> regras, int maxProfundidade = 8)
    {
        ArgumentNullException.ThrowIfNull(fatosObservados);
        ArgumentNullException.ThrowIfNull(regras);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxProfundidade);

        var melhores = new Dictionary<string, Conclusao>(StringComparer.OrdinalIgnoreCase);
        var fronteira = new Queue<(string Fato, double Confianca, List<string> Cadeia, int Nivel)>();

        foreach (var fato in fatosObservados.Where(f => !string.IsNullOrWhiteSpace(f)))
            fronteira.Enqueue((fato.Trim(), 1.0, [$"observado: {fato.Trim()}"], 0));

        while (fronteira.Count > 0)
        {
            var (fato, confianca, cadeia, nivel) = fronteira.Dequeue();
            if (nivel >= maxProfundidade)
                continue;

            foreach (var regra in regras)
            {
                if (!string.Equals(regra.Se, fato, StringComparison.OrdinalIgnoreCase))
                    continue;

                var derivada = confianca * regra.Confianca;
                if (melhores.TryGetValue(regra.Entao, out var atual) && atual.Confianca >= derivada)
                    continue; // já temos caminho melhor — e é isso que corta ciclos

                var passo = string.Create(CultureInfo.InvariantCulture,
                    $"{regra.Se} → {regra.Entao} ({regra.Confianca:0.00}: {regra.Racional})");
                var novaCadeia = new List<string>(cadeia) { passo };

                melhores[regra.Entao] = new Conclusao(regra.Entao, derivada, novaCadeia);
                fronteira.Enqueue((regra.Entao, derivada, novaCadeia, nivel + 1));
            }
        }

        return [.. melhores.Values.OrderByDescending(c => c.Confianca)];
    }

    /// <summary>
    /// Regras semente SMT/SMD: defeito observável → causa física → contramedida.
    /// São o ponto de partida da elicitação (workshops) — a base curada do épico #7
    /// entra por cima destas em runtime.
    /// </summary>
    public static IReadOnlyList<Regra> RegrasSemente { get; } =
    [
        new("SOLDER-BRIDGE", "estêncil desgastado ou entupido", 0.7,
            "ponte de solda recorrente aponta pra janela do estêncil"),
        new("estêncil desgastado ou entupido", "trocar/limpar estêncil e revisar ciclo de vida", 0.9,
            "contramedida direta da causa física"),
        new("INSUFFICIENT-PASTE", "pasta fora da janela de uso", 0.6,
            "volume baixo no SPI com estêncil ok aponta pra pasta"),
        new("pasta fora da janela de uso", "descartar pasta e auditar controle de validade", 0.9,
            "contramedida direta da causa física"),
        new("TOMBSTONE", "perfil térmico desbalanceado no reflow", 0.7,
            "levantamento de componente indica gradiente entre terminais"),
        new("perfil térmico desbalanceado no reflow", "reperfilar forno com placa instrumentada", 0.9,
            "contramedida direta da causa física"),
        new("MISSING-COMPONENT", "feeder vazio ou fita com falha", 0.8,
            "ausência sistemática aponta pro abastecimento"),
    ];
}
