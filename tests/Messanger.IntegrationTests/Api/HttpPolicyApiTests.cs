using System.Net;
using System.Net.Http.Json;
using System.Text;
using Messanger.IntegrationTests.Support;

namespace Messanger.IntegrationTests.Api;

internal static class HttpPolicyApiTests
{
    public static async Task BrowserOriginMustMatchAllowlistExactly(ApiTestHost host)
    {
        foreach (var origin in new[] { "https://evil.example", ApiTestHost.AllowedOrigin + ".evil.example", "null" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { username = "unknown", password = ApiTestHost.TestPassword })
            };
            request.Headers.Add("Origin", origin);
            using var response = await host.Client.SendAsync(request);
            await ApiAssertions.ErrorAsync(response, HttpStatusCode.Forbidden, "origin_not_allowed");
            TestAssert.True(!response.Headers.Contains("Access-Control-Allow-Origin"),
                "Rejected origins must not receive CORS permission.");
        }

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/auth/login");
        preflight.Headers.Add("Origin", ApiTestHost.AllowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var allowed = await host.Client.SendAsync(preflight);
        TestAssert.Equal(HttpStatusCode.NoContent, allowed.StatusCode,
            "Allowed CORS preflight should succeed anonymously.");
        TestAssert.Equal(ApiTestHost.AllowedOrigin, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single(),
            "Only the exact configured origin should be allowed.");
        TestAssert.True(!allowed.Headers.Contains("Access-Control-Allow-Credentials"),
            "Bearer auth does not require CORS cookies.");
    }

    public static async Task ProductionRequiresHttps(ApiTestHost host)
    {
        await using var production = await host.CreateProductionHostAsync();
        using var response = await production.Client.PostAsJsonAsync("http://localhost/registration", new
        {
            username = ApiTestHost.NewUsername(),
            password = ApiTestHost.TestPassword
        });
        await ApiAssertions.ErrorAsync(response, HttpStatusCode.BadRequest, "https_required");

        // HTTPS reaches the real endpoint with the same production configuration.
        await production.RegisterAsync();
    }

    public static async Task MalformedAndOversizedBodiesUseErrorEnvelope(ApiTestHost host)
    {
        using var malformed =
            await host.Client.PostAsync("/registration", new StringContent("{", Encoding.UTF8, "application/json"));
        await ApiAssertions.ErrorAsync(malformed, HttpStatusCode.BadRequest, "bad_request");

        using var oversized = await host.Client.PostAsync("/registration",
            new StringContent(new string('x', 17 * 1024), Encoding.UTF8, "application/json"));
        await ApiAssertions.ErrorAsync(oversized, HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
    }
}