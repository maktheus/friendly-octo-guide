using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Platform.ServiceDefaults;

/// <summary>
/// Cliente mínimo pro KV v2 do OpenBao (API compatível com Vault — sem SDK extra).
/// Sem OpenBao:Addr configurado (dev local sem o container), volta null e quem
/// chamou decide o fallback — mesmo padrão de "fase 0/fase 1" do resto do repo.
/// </summary>
public static class PlatformSecrets
{
    /// <summary>Chave de DEV. Pública por definição (está no repo) — só vale em Development.</summary>
    public const string DevJwtSigningKey = "dev-only-signing-key-with-32-bytes!!";

    /// <summary>
    /// Resolve a chave de assinatura JWT em UM lugar (antes: literal duplicado em 5 hosts).
    /// Ordem: config (populada pelo OpenBao no boot ou por env) → fallback de dev SÓ em
    /// Development → fora disso, boot falha. Assinar token com chave que está num repo
    /// público é equivalente a não assinar; falhar cedo é o único comportamento seguro.
    /// </summary>
    public static string JwtSigningKey(IConfiguration config, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(environment);

        var key = config["Jwt:SigningKey"];
        if (!string.IsNullOrEmpty(key))
            return key;

        if (environment.IsDevelopment())
            return DevJwtSigningKey;

        throw new InvalidOperationException(
            "Jwt:SigningKey ausente. Fora de Development a chave vem do OpenBao/External Secrets " +
            "(platform/jwt#signingKey); para rodar local, exporte ASPNETCORE_ENVIRONMENT=Development.");
    }

    public static async Task<string?> TryGetAsync(IConfiguration config, string path, string key, CancellationToken ct = default)
    {
        var addr = config["OpenBao:Addr"];
        var token = config["OpenBao:Token"];
        if (string.IsNullOrEmpty(addr) || string.IsNullOrEmpty(token))
            return null;

        using var http = new HttpClient { BaseAddress = new Uri(addr) };
        http.DefaultRequestHeaders.Add("X-Vault-Token", token);

        try
        {
            var response = await http.GetAsync($"/v1/secret/data/{path}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadFromJsonAsync<VaultKvResponse>(cancellationToken: ct);
            return body?.Data?.Data?.GetValueOrDefault(key);
        }
        catch (HttpRequestException)
        {
            return null; // OpenBao fora do ar não pode derrubar o boot do serviço
        }
    }

    private sealed class VaultKvResponse
    {
        public VaultKvData? Data { get; set; }
    }

    private sealed class VaultKvData
    {
        public Dictionary<string, string>? Data { get; set; }
    }
}
