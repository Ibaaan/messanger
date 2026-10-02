using System.Net;
using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class ChatApiTests
{
    public static async Task SearchExcludesCallerAndInactiveUsers(ApiTestHost host)
    {
        var prefix = "find_" + Guid.NewGuid().ToString("N")[..6];
        var caller = await host.RegisterAsync(prefix + "a");
        var inactive = await host.RegisterAsync(prefix + "b");
        var recipient = await host.RegisterAsync(prefix + "c");
        await host.SetStatusAsync(inactive.Id, "SUSPENDED");
        var login = await host.LoginAsync(caller);

        using var response = await host.SendAsAsync(HttpMethod.Get, "/api/users/search?query=" + prefix.ToUpperInvariant(), login.AccessToken);
        response.EnsureSuccessStatusCode();
        var users = (await response.Content.ReadFromJsonAsync<UserSummary[]>())!;
        TestAssert.Equal(1, users.Length, "Search should exclude the caller and suspended accounts.");
        TestAssert.Equal(recipient.Id, users[0].Id, "Search should ignore case and return the active match.");
    }

    public static async Task GroupCreationAddsOwnerAndUniqueMembers(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var outsider = await host.RegisterAsync();
        var ownerLogin = await host.LoginAsync(owner);
        var memberLogin = await host.LoginAsync(member);
        var outsiderLogin = await host.LoginAsync(outsider);
        var chat = await CreateAsync(host, ownerLogin.AccessToken, [member.Id, member.Id, owner.Id]);

        TestAssert.Equal(2, chat.Participants.Count, "Members should be deduplicated.");
        TestAssert.Equal("OWNER", chat.Participants.Single(item => item.UserId == owner.Id).Role, "The caller should own the group.");
        using var memberList = await host.SendAsAsync(HttpMethod.Get, "/api/chats", memberLogin.AccessToken);
        var chats = (await memberList.Content.ReadFromJsonAsync<ChatResponse[]>())!;
        TestAssert.True(chats.Any(item => item.Id == chat.Id), "The member should see the new group.");
        using var denied = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}", outsiderLogin.AccessToken);
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task InvalidParticipantsDoNotLeavePartialChats(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var login = await host.LoginAsync(owner);
        using var invalid = await host.SendAsAsync(HttpMethod.Post, "/api/chats/create", login.AccessToken,
            new { chatName = "Invalid", userIds = new[] { long.MaxValue } });
        await ApiAssertions.ErrorAsync(invalid, HttpStatusCode.UnprocessableEntity, "validation_failed");
        var name = "atomic_" + Guid.NewGuid().ToString("N");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IChatRepository>()
                    .CreateAsync(new Chat { Name = name }, owner.Id, [long.MaxValue]);
                throw new InvalidOperationException("An invalid participant FK should be rejected.");
            }
            catch (DbUpdateException) { }
        }
        await using var verification = host.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<MessangerDbContext>();
        TestAssert.True(!await db.Chats.AnyAsync(chat => chat.Name == name), "Failed participant insertion should roll back the chat too.");
    }

    public static async Task HistoryIsChronologicalAndLimitedToFifty(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var login = await host.LoginAsync(owner);
        var chat = await CreateAsync(host, login.AccessToken, [member.Id]);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessageService>();
        var first = await service.SendAsync(chat.Id, owner.Id, "First", null, default);
        var second = await service.SendAsync(chat.Id, owner.Id, "Second", null, default);
        var third = await service.SendAsync(chat.Id, owner.Id, "Third", null, default);

        using var response = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages?limit=2", login.AccessToken);
        var history = (await response.Content.ReadFromJsonAsync<MessageResponse[]>())!;
        TestAssert.Equal(2, history.Length, "The history limit should be honored.");
        TestAssert.Equal(second.Id, history[0].Id, "A page should start with its oldest message.");
        TestAssert.Equal(third.Id, history[1].Id, "A page should end with its newest message.");
        using var older = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages?beforeMessageId={second.Id}", login.AccessToken);
        TestAssert.Equal(first.Id, (await older.Content.ReadFromJsonAsync<MessageResponse[]>())!.Single().Id, "The cursor itself must be excluded.");
        using var tooMany = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages?limit=51", login.AccessToken);
        await ApiAssertions.ErrorAsync(tooMany, HttpStatusCode.UnprocessableEntity, "validation_failed");
    }

    public static async Task DepartedMembersCannotReadOrSend(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var login = await host.LoginAsync(member);
        var chat = await CreateAsync(host, (await host.LoginAsync(owner)).AccessToken, [member.Id]);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var participant = await db.Participants.SingleAsync(item => item.ChatId == chat.Id && item.UserId == member.Id);
        participant.LeftAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        using var denied = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages", login.AccessToken);
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        try
        {
            await scope.ServiceProvider.GetRequiredService<MessageService>().SendAsync(chat.Id, member.Id, "Denied", null, default);
            throw new InvalidOperationException("A departed member must not send messages.");
        }
        catch (UseCaseException exception) when (exception.Code == "forbidden") { }
    }

    public static async Task RepliesCannotReferenceAnotherChat(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var login = await host.LoginAsync(owner);
        var first = await CreateAsync(host, login.AccessToken, [member.Id]);
        var second = await CreateAsync(host, login.AccessToken, [member.Id]);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessageService>();
        var message = await service.SendAsync(first.Id, owner.Id, "Original", null, default);
        try
        {
            await service.SendAsync(second.Id, owner.Id, "Invalid reply", message.Id, default);
            throw new InvalidOperationException("A reply must belong to the same chat.");
        }
        catch (UseCaseException exception) when (exception.Code == "validation_failed") { }
    }

    public static async Task<ChatResponse> CreateAsync(ApiTestHost host, string token, long[] userIds)
    {
        using var response = await host.SendAsAsync(HttpMethod.Post, "/api/chats/create", token,
            new { chatName = "Test group", description = "Plain text chat", userIds });
        TestAssert.Equal(HttpStatusCode.Created, response.StatusCode, "Group creation should succeed.");
        return (await response.Content.ReadFromJsonAsync<ChatResponse>())!;
    }
}
