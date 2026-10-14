using System.Net;
using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Accounts;
using Messanger.Application.Messages;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class AccountApiTests
{
    public static async Task AdministratorSuspensionRevokesSessionsAtomically(ApiTestHost host)
    {
        var (admin, adminToken) = await CreateAdminAsync(host);
        var user = await host.RegisterAsync();
        var oldToken = (await host.LoginAsync(user)).AccessToken;
        var familyId = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            db.RefreshTokens.Add(new RefreshTokenFamily { UserId = user.Id, FamilyId = familyId,
                JtiHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
            await db.SaveChangesAsync();
        }
        using var suspended = await ChangeStatusAsync(host, adminToken, user.Id, "SUSPENDED");
        TestAssert.Equal(HttpStatusCode.OK, suspended.StatusCode, "Administrator can suspend a user.");
        TestAssert.Equal("SUSPENDED", (await suspended.Content.ReadFromJsonAsync<AccountResponse>())!.AccountStatus, "Return the persisted status.");
        using var denied = await host.GetCurrentUserAsync(oldToken);
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            TestAssert.True((await db.RefreshTokens.SingleAsync(token => token.FamilyId == familyId)).RevokedAt is not null, "Suspension revokes refresh families.");
        }
        using var reactivated = await ChangeStatusAsync(host, adminToken, user.Id, "ACTIVE");
        reactivated.EnsureSuccessStatusCode();
        using var stale = await host.GetCurrentUserAsync(oldToken);
        await ApiAssertions.ErrorAsync(stale, HttpStatusCode.Forbidden, "forbidden");
        using var fresh = await host.GetCurrentUserAsync((await host.LoginAsync(user)).AccessToken);
        TestAssert.Equal(HttpStatusCode.OK, fresh.StatusCode, "Reactivation permits a fresh login only.");
    }

    public static async Task AdministratorRoleIsReadFromDatabase(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        var target = await host.RegisterAsync();
        var realLogin = await host.LoginAsync(user);
        var familyId = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(realLogin.AccessToken).Claims.Single(claim => claim.Type == "sid").Value;
        var forgedRole = TestJwt.Create(host, user.Id, payload => { payload["role"] = "ADMIN"; payload["sid"] = familyId; });
        using var denied = await ChangeStatusAsync(host, forgedRole, target.Id, "SUSPENDED");
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        var (admin, token) = await CreateAdminAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            await db.Users.Where(item => item.Id == admin.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Role, "USER"));
        }
        using var demoted = await ChangeStatusAsync(host, token, target.Id, "SUSPENDED");
        await ApiAssertions.ErrorAsync(demoted, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task AccountContractsRejectInvalidTransitions(ApiTestHost host)
    {
        var (admin, token) = await CreateAdminAsync(host);
        var target = await host.RegisterAsync();
        foreach (var (id, status) in new[] { (0L, "ACTIVE"), (target.Id, "DELETED"), (target.Id, "admin"), (admin.Id, "SUSPENDED") })
        {
            using var response = await ChangeStatusAsync(host, token, id, status);
            await ApiAssertions.ErrorAsync(response, HttpStatusCode.UnprocessableEntity, "validation_failed");
        }
        using var missing = await ChangeStatusAsync(host, token, long.MaxValue, "SUSPENDED");
        await ApiAssertions.ErrorAsync(missing, HttpStatusCode.NotFound, "not_found");
        var targetToken = (await host.LoginAsync(target)).AccessToken;
        using var deleted = await host.SendAsAsync(HttpMethod.Delete, "/api/users/me", targetToken);
        TestAssert.Equal(HttpStatusCode.NoContent, deleted.StatusCode, "Self deletion succeeds.");
        using var restore = await ChangeStatusAsync(host, token, target.Id, "ACTIVE");
        await ApiAssertions.ErrorAsync(restore, HttpStatusCode.Conflict, "account_conflict");
    }

    public static async Task DeletionPreservesMessagesAndRequiresOwnershipTransfer(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var ownerDelete = await host.SendAsAsync(HttpMethod.Delete, "/api/users/me", group.OwnerToken);
        await ApiAssertions.ErrorAsync(ownerDelete, HttpStatusCode.Conflict, "owner_transfer_required");
        await using var member = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        var sent = await member.Connection.InvokeAsync<MessageResponse>("SendMessage", group.Chat.Id, "Keep this message");
        var connectionClosed = ClosedAsync(member);
        using var deleted = await host.SendAsAsync(HttpMethod.Delete, "/api/users/me", group.MemberToken);
        await connectionClosed.WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.Equal(HttpStatusCode.NoContent, deleted.StatusCode, "Non-owner can delete their account.");
        using var history = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{group.Chat.Id}/messages", group.OwnerToken);
        var message = (await history.Content.ReadFromJsonAsync<MessageResponse[]>())!.Single(item => item.Id == sent.Id);
        TestAssert.Equal("Keep this message", message.Text, "Other participants retain the message.");
        TestAssert.Equal("deleted_account", message.SenderUsername, "Deleted identity is replaced in history.");
        TestAssert.Equal("Deleted account", message.SenderDisplayName, "Display name must not reveal deleted identity.");
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        TestAssert.Equal("DELETED", (await db.Users.SingleAsync(user => user.Id == group.Member.Id)).AccountStatus, "Deletion is soft.");
        TestAssert.True((await db.Participants.SingleAsync(item => item.UserId == group.Member.Id && item.ChatId == group.Chat.Id)).LeftAt is not null, "Deletion frees group capacity.");
        using var login = await host.Client.PostAsJsonAsync("/auth/login", new { username = group.Member.Username, password = ApiTestHost.TestPassword });
        await ApiAssertions.ErrorAsync(login, HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    public static async Task SuspensionClosesJoinedAndIdleConnections(ApiTestHost host)
    {
        var (_, token) = await CreateAdminAsync(host);
        var group = await ChatTestGroup.CreateAsync(host);
        await using var joined = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await using var idle = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await joined.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        var joinedClosed = ClosedAsync(joined);
        var idleClosed = ClosedAsync(idle);
        using var suspended = await ChangeStatusAsync(host, token, group.Member.Id, "SUSPENDED");
        suspended.EnsureSuccessStatusCode();
        await Task.WhenAll(joinedClosed, idleClosed).WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.Equal(HubConnectionState.Disconnected, joined.Connection.State, "Joined connection closes.");
        TestAssert.Equal(HubConnectionState.Disconnected, idle.Connection.State, "Idle connection also closes.");
    }

    public static async Task ConcurrentStatusChangesCannotReviveOldTokens(ApiTestHost host)
    {
        var (_, token) = await CreateAdminAsync(host);
        var user = await host.RegisterAsync();
        var oldToken = (await host.LoginAsync(user)).AccessToken;
        var responses = await Task.WhenAll(ChangeStatusAsync(host, token, user.Id, "SUSPENDED"), ChangeStatusAsync(host, token, user.Id, "SUSPENDED"));
        foreach (var response in responses) { response.EnsureSuccessStatusCode(); response.Dispose(); }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            TestAssert.Equal(1, (await db.Users.SingleAsync(item => item.Id == user.Id)).SessionVersion, "An idempotent repeated status does not rotate twice.");
        }
        using var restored = await ChangeStatusAsync(host, token, user.Id, "ACTIVE");
        restored.EnsureSuccessStatusCode();
        using var stale = await host.GetCurrentUserAsync(oldToken);
        await ApiAssertions.ErrorAsync(stale, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task ConcurrentDeletionsPreserveAnActiveAdministrator(ApiTestHost host)
    {
        // A separate schema makes these the only administrators, independent of other scenarios.
        await using var isolated = await host.CreateProductionHostAsync();
        var (_, firstToken) = await CreateAdminAsync(isolated);
        var (_, secondToken) = await CreateAdminAsync(isolated);
        var responses = await Task.WhenAll(
            isolated.SendAsAsync(HttpMethod.Delete, "/api/users/me", firstToken),
            isolated.SendAsAsync(HttpMethod.Delete, "/api/users/me", secondToken));
        try
        {
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent), "One administrator can leave.");
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict), "The last administrator remains.");
            await using var scope = isolated.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            TestAssert.Equal(1, await db.Users.CountAsync(user => user.Role == "ADMIN" && user.AccountStatus == "ACTIVE"), "Never delete both administrators concurrently.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static Task ClosedAsync(SignalRTestClient client)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Connection.Closed += _ => { completion.TrySetResult(); return Task.CompletedTask; };
        return completion.Task;
    }

    private static Task<HttpResponseMessage> ChangeStatusAsync(ApiTestHost host, string token, long userId, string status) =>
        host.SendAsAsync(HttpMethod.Patch, $"/api/admin/users/{userId}/status", token, new { accountStatus = status });

    private static async Task<(UserResponse User, string Token)> CreateAdminAsync(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        await db.Users.Where(item => item.Id == user.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Role, "ADMIN"));
        return (user, (await host.LoginAsync(user)).AccessToken);
    }
}
