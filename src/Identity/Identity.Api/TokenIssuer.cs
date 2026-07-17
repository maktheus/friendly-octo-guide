using System.Security.Claims;
using Identity.Domain.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api;

/// <summary>
/// JWT de curta duração (5 min) + refresh token rotativo (7 dias, uso único, família
/// revogada em caso de reuso). A chave simétrica vem da config em dev; em prod, do
/// OpenBao via External Secrets — nunca em env var pura.
/// </summary>
public sealed class TokenIssuer(IConfiguration config)
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly RefreshTokenFamily _refreshTokens = new(RefreshTokenLifetime);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _familyOwner = new();
    private readonly SymmetricSecurityKey _key = new(
        System.Text.Encoding.UTF8.GetBytes(
            config["Jwt:SigningKey"] ?? "dev-only-signing-key-with-32-bytes!!"));

    public (string AccessToken, Guid RefreshToken) IssueFor(UserAccount user)
    {
        var now = DateTimeOffset.UtcNow;
        // Famílias mortas saem junto do dono — sem isso, uma entrada por login fica pra sempre.
        foreach (var dead in _refreshTokens.PruneExpired(now))
            _familyOwner.TryRemove(dead, out _);

        var familyId = Guid.NewGuid();
        _familyOwner[familyId] = user.Username;
        var refresh = _refreshTokens.Issue(familyId, now);
        return (MintAccessToken(user), refresh.Id);
    }

    /// <summary>Rotaciona o refresh token; devolve o dono da família pra reemitir o access token.</summary>
    public (RedeemResult Result, string? Username) Redeem(Guid refreshTokenId)
    {
        var result = _refreshTokens.Redeem(refreshTokenId, DateTimeOffset.UtcNow);
        var username = result.NewToken is { } rotated ? _familyOwner.GetValueOrDefault(rotated.FamilyId) : null;
        if (result.RevokeFamily && result.FamilyId is { } revoked)
            _familyOwner.TryRemove(revoked, out _);
        return (result, username);
    }

    public string MintAccessToken(UserAccount user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
        claims.AddRange(user.Attributes.Select(a => new Claim($"attr:{a.Key}", a.Value)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "identity",
            Audience = "plataforma-linha",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow + AccessTokenLifetime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
    }
}
