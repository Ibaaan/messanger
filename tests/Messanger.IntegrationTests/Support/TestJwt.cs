using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.IntegrationTests.Support;

internal static class TestJwt
{
    public static string Create(ApiTestHost host, long userId, Action<JwtPayload>? change = null,
        string algorithm = SecurityAlgorithms.RsaSha256, string type = "at+jwt", bool wrongKey = false)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new JwtPayload
        {
            ["iss"] = ApiTestHost.Issuer,
            ["aud"] = ApiTestHost.Audience,
            ["sub"] = userId.ToString(CultureInfo.InvariantCulture),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds(),
            ["role"] = "USER",
            ["token_use"] = "access",
            ["session_version"] = 0
        };
        change?.Invoke(payload);
        using var otherKey = RSA.Create(2048);
        SecurityKey key = algorithm == SecurityAlgorithms.HmacSha256
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64))
            : new RsaSecurityKey(wrongKey ? otherKey : host.SigningKey);
        var credentials = algorithm == SecurityAlgorithms.None ? null : new SigningCredentials(key, algorithm);
        var header = new JwtHeader(credentials) { ["typ"] = type };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}