using System.Net;
using Messanger.IntegrationTests.Support;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.IntegrationTests.Api;

internal static class AccessTokenApiTests
{
    public static async Task MissingAndMalformedTokensAreRejected(ApiTestHost host)
    {
        using var missing = await host.Client.GetAsync("/api/users/me");
        using var malformed = await host.GetCurrentUserAsync("not-a-jwt");
        await ApiAssertions.ErrorAsync(missing, HttpStatusCode.Unauthorized, "unauthorized");
        await ApiAssertions.ErrorAsync(malformed, HttpStatusCode.Unauthorized, "unauthorized");
        TestAssert.True(missing.Headers.WwwAuthenticate.Any(header => header.Scheme == "Bearer"),
            "401 responses should advertise Bearer authentication.");
    }

    public static async Task InvalidTokenClaimsAndSignaturesAreRejected(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        var now = DateTimeOffset.UtcNow;
        (string Name, string Token)[] invalid =
        [
            ("issuer", TestJwt.Create(host, user.Id, payload => payload["iss"] = "https://wrong.example")),
            ("audience", TestJwt.Create(host, user.Id, payload => payload["aud"] = "wrong-audience")),
            ("expiry", TestJwt.Create(host, user.Id, payload =>
            {
                payload["nbf"] = now.AddHours(-1).ToUnixTimeSeconds();
                payload["exp"] = now.AddMinutes(-5).ToUnixTimeSeconds();
            })),
            ("not-before",
                TestJwt.Create(host, user.Id, payload => payload["nbf"] = now.AddMinutes(5).ToUnixTimeSeconds())),
            ("refresh use", TestJwt.Create(host, user.Id, payload => payload["token_use"] = "refresh")),
            ("header type", TestJwt.Create(host, user.Id, type: "JWT")),
            ("signature", TestJwt.Create(host, user.Id, wrongKey: true)),
            ("RSA algorithm", TestJwt.Create(host, user.Id, algorithm: SecurityAlgorithms.RsaSha512)),
            ("symmetric algorithm", TestJwt.Create(host, user.Id, algorithm: SecurityAlgorithms.HmacSha256)),
            ("unsigned", TestJwt.Create(host, user.Id, algorithm: SecurityAlgorithms.None)),
            ("missing expiry", TestJwt.Create(host, user.Id, payload => payload.Remove("exp"))),
            ("missing subject", TestJwt.Create(host, user.Id, payload => payload.Remove("sub"))),
            ("invalid subject", TestJwt.Create(host, user.Id, payload => payload["sub"] = "-1")),
            ("missing issue time", TestJwt.Create(host, user.Id, payload => payload.Remove("iat"))),
            ("missing token id", TestJwt.Create(host, user.Id, payload => payload.Remove("jti"))),
            ("missing token use", TestJwt.Create(host, user.Id, payload => payload.Remove("token_use"))),
            ("missing role", TestJwt.Create(host, user.Id, payload => payload.Remove("role")))
        ];
        foreach (var (name, token) in invalid)
        {
            using var response = await host.GetCurrentUserAsync(token);
            TestAssert.Equal(HttpStatusCode.Unauthorized, response.StatusCode,
                $"Token with invalid {name} should be rejected.");
            await ApiAssertions.ErrorAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
        }
    }
}