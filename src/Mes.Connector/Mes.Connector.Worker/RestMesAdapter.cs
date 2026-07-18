using Mes.Connector.Domain;

namespace Mes.Connector.Worker;

/// <summary>
/// Adapter REST do MES real (épico #6): GET {base}/eventos?after={cursor} devolvendo
/// o array que RestMesPayload entende. Autenticação por X-Api-Key quando configurada
/// (Mes:Rest:ApiKey — em prod vem do OpenBao, nunca do appsettings). MES fora do ar
/// propaga exceção: o loop do worker loga e tenta no próximo poll — nunca avança
/// cursor sem ter lido.
/// </summary>
public sealed class RestMesAdapter(HttpClient http) : IMesAdapter
{
    public async Task<IReadOnlyList<RawMesRow>> PollAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        var path = cursor is null
            ? "eventos"
            : $"eventos?after={Uri.EscapeDataString(cursor)}";

        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        return RestMesPayload.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
