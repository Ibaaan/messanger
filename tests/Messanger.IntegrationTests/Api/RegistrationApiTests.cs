using System.Net;
using System.Net.Http.Json;
using Messanger.Application.Authentication;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class RegistrationApiTests
{
    public static async Task CreatesHashedUserAndDefaultProfile(ApiTestHost host)
    {
        var registered = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var stored = await context.Users.SingleAsync(user => user.Id == registered.Id);
        var profile = await context.Set<UserProfile>().SingleAsync(profile => profile.UserId == registered.Id);

        TestAssert.Equal("ru", registered.Language, "Registration should use the specified default language.");
        TestAssert.Equal("ACTIVE", stored.AccountStatus, "New accounts should be active.");
        TestAssert.Equal("USER", stored.Role, "Registration must not grant administrative privileges.");
        TestAssert.Equal(registered.Username, profile.DisplayName, "A default profile should be persisted.");
        TestAssert.True(stored.PasswordHash != ApiTestHost.TestPassword, "A plaintext password must never be stored.");
        TestAssert.True(
            scope.ServiceProvider.GetRequiredService<IPasswordService>().Verify(stored, ApiTestHost.TestPassword),
            "The stored Identity hash should verify the original passphrase.");
    }

    public static async Task DuplicateNamesAreCaseInsensitive(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var response = await host.Client.PostAsJsonAsync("/registration", new
        {
            username = user.Username.ToUpperInvariant(),
            password = ApiTestHost.TestPassword
        });
        await ApiAssertions.ErrorAsync(response, HttpStatusCode.Conflict, "username_taken");
    }

    public static async Task ConcurrentRegistrationsCannotDuplicateAccounts(ApiTestHost host)
    {
        var username = ApiTestHost.NewUsername();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            host.Client.PostAsJsonAsync("/registration", new { username, password = ApiTestHost.TestPassword })));
        try
        {
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created),
                "Exactly one registration should succeed.");
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict),
                "The duplicate registration should return 409.");
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    public static async Task RejectsInvalidCredentialsWithoutCreatingAccounts(ApiTestHost host)
    {
        (string? Username, string? Password)[] invalid =
        [
            (null, ApiTestHost.TestPassword),
            ("ab", ApiTestHost.TestPassword),
            (new string('a', 21), ApiTestHost.TestPassword),
            ("not-valid", ApiTestHost.TestPassword),
            ("имя", ApiTestHost.TestPassword),
            ("abc\n", ApiTestHost.TestPassword),
            (ApiTestHost.NewUsername(), null),
            (ApiTestHost.NewUsername(), "short"),
            (ApiTestHost.NewUsername(), new string(' ', 10)),
            (ApiTestHost.NewUsername(), new string('a', 257))
        ];
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var before = await context.Users.CountAsync();
        foreach (var (username, password) in invalid)
        {
            using var response = await host.Client.PostAsJsonAsync("/registration", new { username, password });
            await ApiAssertions.ErrorAsync(response, HttpStatusCode.UnprocessableEntity, "validation_failed");
        }

        TestAssert.Equal(before, await context.Users.CountAsync(), "Invalid registrations must not create users.");
    }
}