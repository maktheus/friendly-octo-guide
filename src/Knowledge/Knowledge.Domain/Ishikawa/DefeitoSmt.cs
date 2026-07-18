namespace Knowledge.Domain.Ishikawa;

/// <summary>
/// Liga o defeito detectado na inspeção (SPI/AOI, épico #9) à categoria 6M mais
/// provável — é a ponte visão → Ishikawa que alimenta o diagnóstico. Mapa curado
/// com o racional visível (XAI); código desconhecido cai no classificador por
/// palavra-chave e, sem casar, é Indefinida — nunca inventa categoria.
/// </summary>
public static class DefeitoSmt
{
    /// <summary>Mapa curado: código de defeito → (categoria, racional).</summary>
    public static IReadOnlyDictionary<string, (IshikawaCategory Categoria, string Racional)> MapaPadrao { get; } =
        new Dictionary<string, (IshikawaCategory, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["SOLDER-BRIDGE"] = (IshikawaCategory.Maquina,
                "Ponte de solda: tipicamente desgaste/entupimento de estêncil ou pressão de squeegee."),
            ["INSUFFICIENT-PASTE"] = (IshikawaCategory.Material,
                "Pasta insuficiente: pasta fora da janela de uso ou viscosidade degradada."),
            ["EXCESS-PASTE"] = (IshikawaCategory.Metodo,
                "Excesso de pasta: parâmetro de impressão (velocidade/pressão/separação) fora da receita."),
            ["MISALIGNMENT"] = (IshikawaCategory.Maquina,
                "Desalinhamento: calibração do pick-and-place ou fiducial sujo."),
            ["TOMBSTONE"] = (IshikawaCategory.Metodo,
                "Tombstone: perfil térmico do reflow desbalanceado entre os terminais."),
            ["COLD-JOINT"] = (IshikawaCategory.Metodo,
                "Solda fria: perfil de reflow abaixo da zona de fusão."),
            ["MISSING-COMPONENT"] = (IshikawaCategory.Material,
                "Componente ausente: feeder vazio ou fita com falha de bolso."),
        };

    /// <summary>Mapeia um defeito pra categoria 6M com o porquê. Nunca lança; desconhecido explica que é.</summary>
    public static (IshikawaCategory Categoria, string Racional) Map(string tipoDefeito)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoDefeito);

        if (MapaPadrao.TryGetValue(tipoDefeito.Trim(), out var curado))
            return curado;

        var porPalavra = IshikawaClassifier.Classify(tipoDefeito);
        return porPalavra == IshikawaCategory.Indefinida
            ? (IshikawaCategory.Indefinida, $"Defeito '{tipoDefeito}' fora do mapa curado e do vocabulário — elicitar com a operação.")
            : (porPalavra, $"Defeito '{tipoDefeito}' classificado por palavra-chave (fora do mapa curado).");
    }
}
