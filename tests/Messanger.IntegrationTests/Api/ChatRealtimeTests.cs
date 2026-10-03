using System.Net;
using System.Net.Http.Json;
using Messanger.Application.Messages;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class ChatRealtimeTests
{
    public static async Task MembersReceiveMessagesAfterPersistence(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var ownerLogin = await host.LoginAsync(owner);
        var memberLogin = await host.LoginAsync(member);
        var chat = await ChatApiTests.CreateAsync(host, ownerLogin.AccessToken, [member.Id]);
        await using var sender = await SignalRTestClient.ConnectAsync(host, ownerLogin.AccessToken);
        await using var recipient = await SignalRTestClient.ConnectAsync(host, memberLogin.AccessToken);
        await sender.Connection.InvokeAsync("JoinChat", chat.Id);
        await recipient.Connection.InvokeAsync("JoinChat", chat.Id);

        var sent = await sender.Connection.InvokeAsync<MessageResponse>("SendMessage", chat.Id, "<b>plain text</b>");
        var received = await recipient.NextMessageAsync();
        TestAssert.Equal(sent.Id, received.Id, "The recipient should receive the committed message.");
        TestAssert.Equal(owner.Id, received.SenderId, "Sender identity must come from the access token.");
        using var history = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages", memberLogin.AccessToken);
        var stored = (await history.Content.ReadFromJsonAsync<MessageResponse[]>())!.Single();
        TestAssert.Equal(sent.Id, stored.Id, "A delivered message should already be readable from history.");
        TestAssert.Equal("<b>plain text</b>", stored.Text, "Message text should not be treated as markup.");
    }

    public static async Task OutsidersCannotJoinOrSend(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var outsider = await host.RegisterAsync();
        var chat = await ChatApiTests.CreateAsync(host, (await host.LoginAsync(owner)).AccessToken, [member.Id]);
        await using var client = await SignalRTestClient.ConnectAsync(host, (await host.LoginAsync(outsider)).AccessToken);
        await MustRejectAsync(() => client.Connection.InvokeAsync("JoinChat", chat.Id), "forbidden");
        await MustRejectAsync(() => client.Connection.InvokeAsync("SendMessage", chat.Id, "Denied"), "forbidden");
    }

    public static async Task DepartedSubscribersLoseDeliveryAndSendAccess(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var ownerLogin = await host.LoginAsync(owner);
        var memberLogin = await host.LoginAsync(member);
        var chat = await ChatApiTests.CreateAsync(host, ownerLogin.AccessToken, [member.Id]);
        await using var sender = await SignalRTestClient.ConnectAsync(host, ownerLogin.AccessToken);
        await using var departed = await SignalRTestClient.ConnectAsync(host, memberLogin.AccessToken);
        await sender.Connection.InvokeAsync("JoinChat", chat.Id);
        await departed.Connection.InvokeAsync("JoinChat", chat.Id);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            var participant = await db.Participants.SingleAsync(item => item.ChatId == chat.Id && item.UserId == member.Id);
            participant.LeftAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await sender.Connection.InvokeAsync("SendMessage", chat.Id, "For current members only");
        await sender.NextMessageAsync();
        await MustRejectAsync(() => departed.Connection.InvokeAsync("SendMessage", chat.Id, "Denied"), "forbidden");
        TestAssert.True(!departed.Messages.Reader.TryRead(out _), "A stale subscription must not receive new messages.");
    }

    public static async Task ReconnectedMemberCanRecoverMissedHistory(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var ownerLogin = await host.LoginAsync(owner);
        var memberLogin = await host.LoginAsync(member);
        var chat = await ChatApiTests.CreateAsync(host, ownerLogin.AccessToken, [member.Id]);
        await using var sender = await SignalRTestClient.ConnectAsync(host, ownerLogin.AccessToken);
        await using (var disconnected = await SignalRTestClient.ConnectAsync(host, memberLogin.AccessToken))
            await disconnected.Connection.InvokeAsync("JoinChat", chat.Id);
        var sent = await sender.Connection.InvokeAsync<MessageResponse>("SendMessage", chat.Id, "While you were away");
        await using var reconnected = await SignalRTestClient.ConnectAsync(host, memberLogin.AccessToken);
        await reconnected.Connection.InvokeAsync("JoinChat", chat.Id);
        using var response = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chat.Id}/messages", memberLogin.AccessToken);
        TestAssert.Equal(sent.Id, (await response.Content.ReadFromJsonAsync<MessageResponse[]>())!.Single().Id, "History must recover text missed while disconnected.");
    }

    public static async Task NewChatNotifiesItsMembers(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        await using var recipient = await SignalRTestClient.ConnectAsync(host, (await host.LoginAsync(member)).AccessToken);
        var chat = await ChatApiTests.CreateAsync(host, (await host.LoginAsync(owner)).AccessToken, [member.Id]);
        var notice = await recipient.Chats.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.Equal(chat.Id, notice.Id, "A new group should appear for an already-connected recipient.");
    }

    public static async Task HubNegotiationRequiresAuthentication(ApiTestHost host)
    {
        using var missing = await host.Client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);
        await ApiAssertions.ErrorAsync(missing, HttpStatusCode.Unauthorized, "unauthorized");
        var user = await host.RegisterAsync();
        var login = await host.LoginAsync(user);
        using var wrongPath = await host.Client.GetAsync("/api/users/me?access_token=" + login.AccessToken);
        await ApiAssertions.ErrorAsync(wrongPath, HttpStatusCode.Unauthorized, "unauthorized");
    }

    private static async Task MustRejectAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (HubException exception) when (exception.Message.Contains(code, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Hub operation should fail with " + code);
    }
}
