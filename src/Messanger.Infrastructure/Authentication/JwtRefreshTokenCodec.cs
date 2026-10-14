using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Messanger.Application;
using Messanger.Application.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.Infrastructure.Authentication;

internal sealed class JwtRefreshTokenCodec(JwtSigningKey signingKey, IOptions<JwtOptions> options, TimeProvider clock) : IRefreshTokenCodec
{
    public SignedRefreshToken Issue(long userId, int sessionVersion, Guid familyId, DateTimeOffset expiresAt)
    {
        var now = clock.GetUtcNow();
        if (expiresAt <= now) throw new UseCaseException("session_expired", "Your session expired. Please log in again.");
        var jti = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer, Audience = options.Value.Audience + ":refresh",
            Subject = new ClaimsIdentity([
                new Claim("sub", userId.ToString(CultureInfo.InvariantCulture)), new Claim("jti", jti),
                new Claim("sid", familyId.ToString()), new Claim("session_version", sessionVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim("token_use", "refresh")]),
            IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime, Expires = expiresAt.UtcDateTime,
            TokenType = "rt+jwt", SigningCredentials = new(signingKey.Key, SecurityAlgorithms.RsaSha256)
        };
        return new(new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), new(userId, sessionVersion, familyId, Hash(jti), expiresAt));
    }

    public RefreshCredential? Validate(string? token, bool validateLifetime = true)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = options.Value.Issuer,
                ValidateAudience = true, ValidAudience = options.Value.Audience + ":refresh",
                ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey.Key,
                RequireSignedTokens = true, RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidTypes = ["rt+jwt"],
                // Use the injected clock and strict family deadline, including in integration checks.
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) => expires is not null && (!validateLifetime ||
                    (expires > clock.GetUtcNow().UtcDateTime && (notBefore is null || notBefore <= clock.GetUtcNow().UtcDateTime)))
            }, out var validated);
            if (principal.FindFirst("token_use")?.Value != "refresh"
                || !long.TryParse(principal.FindFirst("sub")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0
                || !int.TryParse(principal.FindFirst("session_version")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 0
                || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var familyId) || familyId == Guid.Empty
                || principal.FindFirst("jti")?.Value is not { Length: 64 } jti)
                return null;
            return new(userId, version, familyId, Hash(jti), new DateTimeOffset(validated.ValidTo, TimeSpan.Zero));
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException) { return null; }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
