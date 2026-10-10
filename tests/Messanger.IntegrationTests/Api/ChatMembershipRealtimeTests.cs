using System.Net;
using Messanger.Application.Messages;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messanger.IntegrationTests.Api;

internal static class ChatMembershipRealtimeTests
{
    public static async Task RemovalRevokesEveryJoinedConnectionImmediately(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await using var owner = await SignalRTestClient.ConnectAsync(host, group.OwnerToken);
        await using var firstTab = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await using var secondTab = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        foreach (var client in new[] { owner, firstTab, secondTab }) await client.Connection.InvokeAsync("JoinChat", group.Chat.Id);

        using var removed = await host.SendAsAsync(HttpMethod.Delete, $"/api/chats/{group.Chat.Id}/members/{group.Member.Id}", group.OwnerToken);
        TestAssert.Equal(HttpStatusCode.NoContent, removed.StatusCode, "Owner removal should commit.");
        foreach (var client in new[] { firstTab, secondTab })
            TestAssert.Equal(group.Chat.Id, await client.RevokedChats.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)), "Every tab must be told its chat access was revoked.");
        TestAssert.Equal(group.Chat.Id, await owner.ChangedChats.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)), "Current members should get a membership update.");
        await owner.Connection.InvokeAsync("SendMessage", group.Chat.Id, "After removal");
        await owner.NextMessageAsync();
        TestAssert.True(!firstTab.Messages.Reader.TryRead(out _) && !secondTab.Messages.Reader.TryRead(out _), "Removed subscribers must not receive later messages.");
        await MustRejectAsync(() => firstTab.Connection.InvokeAsync("JoinChat", group.Chat.Id));
        await MustRejectAsync(() => secondTab.Connection.InvokeAsync("SendMessage", group.Chat.Id, "Denied"));
    }

    public static async Task RejoinedMemberMustJoinAgainBeforeDelivery(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await using var owner = await SignalRTestClient.ConnectAsync(host, group.OwnerToken);
        await using var member = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await owner.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        await member.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        using var leave = await host.SendAsAsync(HttpMethod.Post, $"/api/chats/{group.Chat.Id}/leave", group.MemberToken);
        leave.EnsureSuccessStatusCode();
        await member.RevokedChats.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        using var rejoin = await host.SendAsAsync(HttpMethod.Post, $"/api/chats/{group.Chat.Id}/members", group.OwnerToken, new { userId = group.Member.Id });
        rejoin.EnsureSuccessStatusCode();
        await owner.Connection.InvokeAsync("SendMessage", group.Chat.Id, "Before subscribing again");
        await owner.NextMessageAsync();
        TestAssert.True(!member.Messages.Reader.TryRead(out _), "Rejoining must not silently restore a stale live subscription.");
        await member.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        var sent = await owner.Connection.InvokeAsync<MessageResponse>("SendMessage", group.Chat.Id, "After subscribing again");
        TestAssert.Equal(sent.Id, (await member.NextMessageAsync()).Id, "A freshly authorized subscription should work after rejoin.");
    }

    public static async Task InvalidHubArgumentsUseStableBusinessErrors(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await using var owner = await SignalRTestClient.ConnectAsync(host, group.OwnerToken);
        await ExpectCodeAsync(() => owner.Connection.InvokeAsync("JoinChat", 0L), "validation_failed");
        await ExpectCodeAsync(() => owner.Connection.InvokeAsync("LeaveChat", -1L), "validation_failed");
        await ExpectCodeAsync(() => owner.Connection.InvokeAsync("SendMessage", group.Chat.Id, "  "), "validation_failed");
    }

    private static Task MustRejectAsync(Func<Task> action) => ExpectCodeAsync(action, "forbidden");

    private static async Task ExpectCodeAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (HubException exception) when (exception.Message.Contains(code + ":", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected a safe hub error with code " + code);
    }
}
