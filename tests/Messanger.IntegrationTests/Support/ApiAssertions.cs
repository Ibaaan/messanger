using System.Net;
using System.Text.Json;

namespace Messanger.IntegrationTests.Support;

internal static class ApiAssertions
{
    public static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string code)
    {
        TestAssert.Equal(expectedStatus, response.StatusCode, "Unexpected API status.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = body.RootElement.GetProperty("error");
        TestAssert.Equal(code, error.GetProperty("code").GetString(), "Unexpected API error code.");
        TestAssert.True(!string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()),
            "Errors should contain a request ID.");
        TestAssert.True(!string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()),
            "Errors should contain a safe message.");
    }
}