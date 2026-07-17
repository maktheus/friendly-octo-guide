using System.Diagnostics.CodeAnalysis;

namespace Mes.Connector.Domain;

/// <summary>Progresso do polling: o último cursor já processado. DTO puro.</summary>
[ExcludeFromCodeCoverage]
public sealed record MesPollState(string? LastCursor)
{
    public static MesPollState Start { get; } = new((string?)null);
}

/// <summary>
/// Idempotência do polling: dado um lote de linhas e o estado, devolve só as linhas
/// NOVAS (cursor &gt; último) e o próximo estado. Reprocesso (adapter reenviando o mesmo
/// lote) nunca republica. Pura. Cursor precisa ser monotônico crescente — timestamp ISO
/// ou id numérico; id numérico é comparado como número, o resto ordinalmente.
/// </summary>
public static class PollCursor
{
    public static (IReadOnlyList<RawMesRow> Fresh, MesPollState Next) SelectNew(
        IReadOnlyList<RawMesRow> rows, MesPollState state)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(state);

        var fresh = new List<RawMesRow>();
        var maxCursor = state.LastCursor;

        foreach (var row in rows)
        {
            if (state.LastCursor is not null && Compare(row.Cursor, state.LastCursor) <= 0)
                continue;

            fresh.Add(row);
            if (maxCursor is null || Compare(row.Cursor, maxCursor) > 0)
                maxCursor = row.Cursor;
        }

        return (fresh, new MesPollState(maxCursor));
    }

    /// <summary>
    /// Se os dois cursores são só dígitos, compara como número — "100" &gt; "99" mesmo sem
    /// zero-padding. Ordinal puro faria o id sem padding "voltar no tempo" e eventos novos
    /// seriam descartados em silêncio, quebrando o "nunca perde". Formato misto cai no ordinal.
    /// </summary>
    public static int Compare(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (IsDigits(a) && IsDigits(b))
        {
            var (ta, tb) = (a.TrimStart('0'), b.TrimStart('0'));
            return ta.Length != tb.Length ? ta.Length - tb.Length : string.CompareOrdinal(ta, tb);
        }

        return string.CompareOrdinal(a, b);
    }

    private static bool IsDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
}
