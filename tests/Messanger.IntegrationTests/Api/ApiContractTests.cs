using System.Net;
using System.Text.Json;
using Messanger.IntegrationTests.Support;

namespace Messanger.IntegrationTests.Api;

internal static class ApiContractTests
{
    public static async Task DevelopmentPublishesAuthenticatedContracts(ApiTestHost host)
    {
        using var response = await host.Client.GetAsync("/openapi/v1.json");
        TestAssert.Equal(HttpStatusCode.OK, response.StatusCode, "Development should expose the contract.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        TestAssert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("Bearer").GetProperty("scheme").GetString()!, "Document JWT authentication.");
        var paths = root.GetProperty("paths");
        foreach (var (path, method) in new[] {
            ("/api/chats/{chatId}/members", "post"),
            ("/api/admin/users/{userId}/status", "patch"),
            ("/api/users/me", "delete"),
            ("/api/chats/{chatId}/members/{userId}", "delete"),
            ("/api/chats/{chatId}/ownership", "post"),
            ("/api/chats/{chatId}/members/{userId}/role", "patch"),
            ("/api/chats/{chatId}/leave", "post") })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            TestAssert.True(operation.GetProperty("security").GetArrayLength() > 0, "Group routes require bearer auth.");
            TestAssert.True(operation.GetProperty("responses").TryGetProperty("422", out _), "Document validation errors.");
        }
        TestAssert.True(!paths.GetProperty("/auth/login").GetProperty("post").TryGetProperty("security", out _),
            "Login must remain anonymous.");
        TestAssert.True(paths.GetProperty("/registration").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _),
            "Registration documents Created.");
    }

    public static async Task ProductionDoesNotPublishContracts(ApiTestHost host)
    {
        await using var production = await host.CreateProductionHostAsync();
        var user = await production.RegisterAsync();
        var login = await production.LoginAsync(user);
        using var response = await production.SendAsAsync(HttpMethod.Get, "/openapi/v1.json", login.AccessToken);
        TestAssert.Equal(HttpStatusCode.NotFound, response.StatusCode, "Production must not expose development contracts.");
    }
}
