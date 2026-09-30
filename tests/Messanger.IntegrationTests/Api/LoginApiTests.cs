using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Authentication;
using Messanger.IntegrationTests.Support;

namespace Messanger.IntegrationTests.Api;

internal static class LoginApiTests
{
    public static async Task AccessTokenAuthenticatesOnlyItsOwner(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var loginResponse = await host.Client.PostAsJsonAsync("/auth/login", new
        {
            username = user.Username.ToUpperInvariant(),
            password = ApiTestHost.TestPassword
        });
        TestAssert.Equal(HttpStatusCode.OK, loginResponse.StatusCode, "Login should accept different username casing.");
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        TestAssert.Equal("RS256", jwt.Header.Alg, "Access tokens must use RS256.");
        TestAssert.Equal("at+jwt", jwt.Header.Typ, "Access tokens must declare their type.");
        TestAssert.Equal("access", jwt.Claims.Single(claim => claim.Type == "token_use").Value,
            "Access token use is required.");
        TestAssert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom,
            "Access lifetime should be fifteen minutes.");
        TestAssert.True(!jwt.Claims.Any(claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase)),
            "Tokens must not contain credentials.");
        using var response = await host.GetCurrentUserAsync(login.AccessToken);
        TestAssert.Equal(HttpStatusCode.OK, response.StatusCode, "Issued token should authenticate.");
        var current = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        TestAssert.Equal(user.Id, current.Id, "Identity must come from the validated token subject.");
        TestAssert.True(response.Headers.CacheControl?.NoStore == true, "Authenticated responses must not be cached.");
    }

    public static async Task InvalidAndInactiveAccountsReturnGenericErrors(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var wrongPassword = await host.Client.PostAsJsonAsync("/auth/login",
            new { username = user.Username, password = "incorrect password" });
        using var unknownUser = await host.Client.PostAsJsonAsync("/auth/login",
            new { username = ApiTestHost.NewUsername(), password = ApiTestHost.TestPassword });
        await ApiAssertions.ErrorAsync(wrongPassword, HttpStatusCode.Unauthorized, "invalid_credentials");
        await ApiAssertions.ErrorAsync(unknownUser, HttpStatusCode.Unauthorized, "invalid_credentials");
        foreach (var status in new[] { "SUSPENDED", "DELETED" })
        {
            await host.SetStatusAsync(user.Id, status);
            using var response = await host.Client.PostAsJsonAsync("/auth/login",
                new { username = user.Username, password = ApiTestHost.TestPassword });
            await ApiAssertions.ErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_credentials");
        }
    }

    public static async Task SuspendedUserCannotUsePreviouslyIssuedToken(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        var login = await host.LoginAsync(user);
        await host.SetStatusAsync(user.Id, "SUSPENDED");

        using var response = await host.GetCurrentUserAsync(login.AccessToken);
        await ApiAssertions.ErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }
}