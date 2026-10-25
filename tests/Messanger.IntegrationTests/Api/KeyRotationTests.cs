using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Messanger.Application.Authentication;
using Messanger.Domain;
using Messanger.Infrastructure.Authentication;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class KeyRotationTests
{
    public static async Task PreviousPublicKeysValidateAccessAndRefreshDuringRotation(ApiTestHost host)
    {
        var directory = Directory.CreateTempSubdirectory("messanger-rotation-");
        using var old = RSA.Create(2048);
        using var current = RSA.Create(2048);
        try
        {
            var oldPrivate = Path.Combine(directory.FullName, "old.pem");
            var oldPublic = Path.Combine(directory.FullName, "old.pub");
            var currentPrivate = Path.Combine(directory.FullName, "current.pem");
            await File.WriteAllTextAsync(oldPrivate, old.ExportPkcs8PrivateKeyPem());
            await File.WriteAllTextAsync(oldPublic, old.ExportSubjectPublicKeyInfoPem());
            await File.WriteAllTextAsync(currentPrivate, current.ExportPkcs8PrivateKeyPem());
            await using var rotated = await host.CreateSecurityHostAsync(options => options.Enabled = false, overrides: services =>
                services.PostConfigure<JwtOptions>(options => { options.PrivateKeyPath = currentPrivate; options.PreviousPublicKeyPaths = [oldPublic]; }));
            var user = await rotated.RegisterAsync();
            var login = await rotated.LoginAsync(user);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
            var family = jwt.Claims.Single(claim => claim.Type == "sid").Value;
            var legacyAccess = TestJwt.Create(rotated, user.Id, payload => payload["sid"] = family, signingKey: old);
            using var accepted = await rotated.GetCurrentUserAsync(legacyAccess);
            accepted.EnsureSuccessStatusCode();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Jwt:PrivateKeyPath"] = oldPrivate, ["Jwt:Issuer"] = ApiTestHost.Issuer, ["Jwt:Audience"] = ApiTestHost.Audience
            }).Build();
            await using var previousServices = new ServiceCollection().AddAuthenticationInfrastructure(configuration).BuildServiceProvider();
            var previous = previousServices.GetRequiredService<IRefreshTokenCodec>().Issue(user.Id, 0, Guid.NewGuid(),
                DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()));
            await using (var scope = rotated.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
                db.RefreshTokens.Add(new RefreshTokenFamily { UserId = user.Id, FamilyId = previous.Credential.FamilyId, JtiHash = previous.Credential.JtiHash, ExpiresAt = previous.Credential.ExpiresAt });
                await db.SaveChangesAsync();
            }
            var cookies = new CookieContainer();
            cookies.Add(new Cookie("__Secure-MessangerRefresh", previous.Value, "/auth", "localhost") { Secure = true, HttpOnly = true });
            using var browser = await SessionBrowser.CreateAsync(rotated, cookies);
            using var response = await browser.Client.PostAsync("/auth/refresh", null);
            response.EnsureSuccessStatusCode();
            var renewed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
            TestAssert.Equal(jwt.Header.Kid, new JwtSecurityTokenHandler().ReadJwtToken(renewed.AccessToken).Header.Kid, "Old refresh credentials rotate onto the current signing key.");
        }
        finally { directory.Delete(recursive: true); }
    }
}
