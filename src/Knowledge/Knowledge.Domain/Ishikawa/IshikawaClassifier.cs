using System.Text;

namespace Knowledge.Domain.Ishikawa;

/// <summary>
/// Classifica um sintoma (código de defeito, motivo de parada, texto) numa categoria
/// 6M por palavra-chave. É a semente do sistema especialista (Épico 5): regras
/// interpretáveis e explicáveis (XAI), não caixa-preta. A base cresce com a elicitação
/// da operação. Pura e determinística.
/// </summary>
public static class IshikawaClassifier
{
    /// <summary>Regras semente com vocabulário de linha SMT/SMD. Palavra (minúscula) → categoria.</summary>
    public static IReadOnlyList<(string Palavra, IshikawaCategory Categoria)> RegrasPadrao { get; } =
    [
        ("estencil", IshikawaCategory.Maquina),
        ("desgaste", IshikawaCategory.Maquina),
        ("squeegee", IshikawaCategory.Maquina),
        ("nozzle", IshikawaCategory.Maquina),
        ("pick", IshikawaCategory.Maquina),
        ("pasta", IshikawaCategory.Material),
        ("solda", IshikawaCategory.Material),
        ("componente", IshikawaCategory.Material),
        ("umidade", IshikawaCategory.MeioAmbiente),
        ("temperatura", IshikawaCategory.MeioAmbiente),
        ("operador", IshikawaCategory.MaoDeObra),
        ("turno", IshikawaCategory.MaoDeObra),
        ("treinamento", IshikawaCategory.MaoDeObra),
        ("spi", IshikawaCategory.Medicao),
        ("aoi", IshikawaCategory.Medicao),
        ("calibr", IshikawaCategory.Medicao),
        ("medicao", IshikawaCategory.Medicao),
        ("setup", IshikawaCategory.Metodo),
        ("procedimento", IshikawaCategory.Metodo),
        ("receita", IshikawaCategory.Metodo),
    ];

    /// <summary>Classifica com as regras semente.</summary>
    public static IshikawaCategory Classify(string sintomaOuCodigo) =>
        Classify(sintomaOuCodigo, RegrasPadrao);

    /// <summary>Classifica com um conjunto de regras (a ordem decide o empate: primeira que casa vence).</summary>
    public static IshikawaCategory Classify(
        string sintomaOuCodigo, IReadOnlyList<(string Palavra, IshikawaCategory Categoria)> regras)
    {
        ArgumentNullException.ThrowIfNull(sintomaOuCodigo);
        ArgumentNullException.ThrowIfNull(regras);

        var texto = Fold(sintomaOuCodigo);
        foreach (var (palavra, categoria) in regras)
        {
            if (texto.Contains(Fold(palavra), StringComparison.Ordinal))
                return categoria;
        }

        return IshikawaCategory.Indefinida;
    }

    /// <summary>
    /// Minúsculas + remoção de diacríticos: "medição", "MEDIÇÃO" e "medicao" são a mesma
    /// palavra. Sem isso, cada regra teria que listar todas as grafias acentuadas — e o
    /// texto vindo da operação nunca é consistente nisso. Mapa explícito porque o repo
    /// compila com InvariantGlobalization e ali string.Normalize() é no-op.
    /// </summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s)
        {
            var ch = raw switch
            {
                'á' or 'à' or 'â' or 'ã' or 'ä' or 'Á' or 'À' or 'Â' or 'Ã' or 'Ä' => 'a',
                'é' or 'è' or 'ê' or 'ë' or 'É' or 'È' or 'Ê' or 'Ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' or 'Í' or 'Ì' or 'Î' or 'Ï' => 'i',
                'ó' or 'ò' or 'ô' or 'õ' or 'ö' or 'Ó' or 'Ò' or 'Ô' or 'Õ' or 'Ö' => 'o',
                'ú' or 'ù' or 'û' or 'ü' or 'Ú' or 'Ù' or 'Û' or 'Ü' => 'u',
                'ç' or 'Ç' => 'c',
                'ñ' or 'Ñ' => 'n',
                >= (char)0x0300 and <= (char)0x036F => '\0', // marca combinante (texto decomposto): descarta
                _ => char.ToLowerInvariant(raw),
            };

            if (ch != '\0')
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
