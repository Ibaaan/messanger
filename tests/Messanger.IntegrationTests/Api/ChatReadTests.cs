using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class ChatReadTests
{
    public static async Task UnreadCountsIncludeOwnMessagesAndFetchingDoesNotMarkRead(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        var empty = await DetailsAsync(host, group.Chat.Id, group.MemberToken);
        TestAssert.Equal(0L, empty.Chat.UnreadCount, "Empty chats start with no unread messages.");
        var sent = await SendAsync(host, group.Chat.Id, group.Owner.Id, group.Member.Id);
        using var list = await host.SendAsAsync(HttpMethod.Get, "/api/chats", group.MemberToken);
        var chat = (await list.Content.ReadFromJsonAsync<ChatResponse[]>())!.Single(item => item.Id == group.Chat.Id);
        TestAssert.Equal(2L, chat.UnreadCount, "Own messages also count until explicitly read.");
        var details = await DetailsAsync(host, group.Chat.Id, group.MemberToken);
        TestAssert.Equal(2L, details.Chat.UnreadCount, "Fetching history must not acknowledge reading.");
        TestAssert.True(details.Chat.LastReadMessageId is null, "No implicit marker should be created.");
        await using var client = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        var marked = await client.Connection.InvokeAsync<ChatReadState>("MarkRead", group.Chat.Id, sent[0].Id);
        TestAssert.Equal(1L, marked.UnreadCount, "Only messages after the acknowledged ID remain unread.");
        TestAssert.Equal(sent[0].Id, marked.LastReadMessageId!.Value, "Return the persisted marker.");
    }

    public static async Task ReadMarkersAreMonotonicAndNoOpsKeepTheirTimestamp(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        var sent = await SendAsync(host, group.Chat.Id, group.Owner.Id, group.Owner.Id, group.Member.Id);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ChatReadService>();
        var first = await service.MarkReadAsync(group.Chat.Id, group.Member.Id, sent[1].Id, default);
        TestAssert.Equal(1L, first.UnreadCount, "A marker may acknowledge only part of the history.");
        var before = await StoredAsync(host, group.Chat.Id, group.Member.Id);
        var backwards = await service.MarkReadAsync(group.Chat.Id, group.Member.Id, sent[0].Id, default);
        TestAssert.Equal(sent[1].Id, backwards.LastReadMessageId!.Value, "An older tab cannot move the marker backward.");
        await service.MarkReadAsync(group.Chat.Id, group.Member.Id, sent[1].Id, default);
        TestAssert.Equal(before.LastReadAt, (await StoredAsync(host, group.Chat.Id, group.Member.Id)).LastReadAt, "No-op reads preserve their update time.");
        var latest = await service.MarkReadAsync(group.Chat.Id, group.Member.Id, sent[2].Id, default);
        TestAssert.Equal(0L, latest.UnreadCount, "Advancing to the latest message clears unread state.");
    }

    public static async Task ReadContractsRejectInvalidMessagesAndOutsiders(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        var otherChat = await ChatApiTests.CreateAsync(host, group.OwnerToken, [group.Member.Id]);
        var foreign = (await SendAsync(host, otherChat.Id, group.Owner.Id))[0];
        await using var member = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        foreach (var id in new[] { 0L, -1L, long.MaxValue, foreign.Id })
            await RejectAsync(() => member.Connection.InvokeAsync("MarkRead", group.Chat.Id, id), "validation_failed");
        await RejectAsync(() => member.Connection.InvokeAsync("MarkRead", 0L, foreign.Id), "validation_failed");
        await using var outsider = await SignalRTestClient.ConnectAsync(host, group.OtherToken);
        await RejectAsync(() => outsider.Connection.InvokeAsync("MarkRead", group.Chat.Id, foreign.Id), "forbidden");
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var participant = await db.Participants.SingleAsync(item => item.ChatId == group.Chat.Id && item.UserId == group.Member.Id);
        TestAssert.True(!await db.Set<ChatLastRead>().AnyAsync(read => read.ChatParticipantId == participant.Id), "Rejected updates must not create markers.");
    }

    public static async Task ConcurrentTabsCreateOneMarkerAtTheGreatestReadId(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        var sent = await SendAsync(host, group.Chat.Id, group.Owner.Id, group.Owner.Id, group.Owner.Id);
        await using var first = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await using var second = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await Task.WhenAll(first.Connection.InvokeAsync("MarkRead", group.Chat.Id, sent[2].Id),
            second.Connection.InvokeAsync("MarkRead", group.Chat.Id, sent[0].Id));
        var stored = await StoredAsync(host, group.Chat.Id, group.Member.Id);
        TestAssert.Equal(sent[2].Id, stored.LastReadMessageId!.Value, "Concurrent reads must retain the greatest ID in a single row.");
        var details = await DetailsAsync(host, group.Chat.Id, group.MemberToken);
        TestAssert.Equal(0L, details.Chat.UnreadCount, "Counts use the final serialized marker.");
    }

    public static async Task LiveSummariesArePersonalizedAndReadChangesReachEveryReaderTab(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await using var owner = await SignalRTestClient.ConnectAsync(host, group.OwnerToken);
        await using var member = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await using var memberTab = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        var first = await owner.Connection.InvokeAsync<MessageResponse>("SendMessage", group.Chat.Id, "First");
        foreach (var client in new[] { owner, member, memberTab })
            TestAssert.Equal(1L, (await UpdateAsync(client)).UnreadCount, "Chat-list notifications do not require joining the chat.");
        await owner.Connection.InvokeAsync("MarkRead", group.Chat.Id, first.Id);
        TestAssert.Equal(0L, (await UpdateAsync(owner)).UnreadCount, "The reader sees their count clear.");
        var second = await owner.Connection.InvokeAsync<MessageResponse>("SendMessage", group.Chat.Id, "Second");
        var ownerSummary = await UpdateAsync(owner);
        TestAssert.Equal(1L, ownerSummary.UnreadCount, "The owner has read the first message.");
        TestAssert.Equal(first.Id, ownerSummary.LastReadMessageId!.Value, "Summaries contain only that user's marker.");
        foreach (var client in new[] { member, memberTab })
        {
            var update = await UpdateAsync(client);
            TestAssert.Equal(2L, update.UnreadCount, "Unread state differs per participant.");
            TestAssert.Equal(second.Id, update.LastMessage!.Id, "Include the latest preview.");
            TestAssert.Equal("TEXT", update.MessageType!, "Include the implemented message type.");
            TestAssert.True(update.LastReadMessageId is null, "Do not expose another participant's marker.");
        }
        await member.Connection.InvokeAsync("MarkRead", group.Chat.Id, second.Id);
        foreach (var client in new[] { member, memberTab })
            TestAssert.Equal(0L, (await UpdateAsync(client)).UnreadCount, "All reader tabs receive their updated count.");
        TestAssert.Equal(1L, (await DetailsAsync(host, group.Chat.Id, group.OwnerToken)).Chat.UnreadCount, "Reading as one user must not change another user's count.");
    }

    public static async Task RejoiningRetainsReadProgressAndCountsMessagesSentWhileAway(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        var first = (await SendAsync(host, group.Chat.Id, group.Owner.Id))[0];
        await using var member = await SignalRTestClient.ConnectAsync(host, group.MemberToken);
        await member.Connection.InvokeAsync("MarkRead", group.Chat.Id, first.Id);
        using var leave = await host.SendAsAsync(HttpMethod.Post, $"/api/chats/{group.Chat.Id}/leave", group.MemberToken);
        leave.EnsureSuccessStatusCode();
        await SendAsync(host, group.Chat.Id, group.Owner.Id);
        using var rejoin = await host.SendAsAsync(HttpMethod.Post, $"/api/chats/{group.Chat.Id}/members", group.OwnerToken, new { userId = group.Member.Id });
        rejoin.EnsureSuccessStatusCode();
        var state = (await DetailsAsync(host, group.Chat.Id, group.MemberToken)).Chat;
        TestAssert.Equal(first.Id, state.LastReadMessageId!.Value, "Rejoining reuses the participant's previous marker.");
        TestAssert.Equal(1L, state.UnreadCount, "Messages sent while away follow the same ID-based unread rule.");
    }

    public static async Task RepositoryRechecksAccessAfterRemovalOrSuspension(ApiTestHost host)
    {
        foreach (var suspend in new[] { false, true })
        {
            var group = await ChatTestGroup.CreateAsync(host);
            var sent = (await SendAsync(host, group.Chat.Id, group.Owner.Id))[0];
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            await db.Users.SingleAsync(user => user.Id == group.Member.Id);
            await db.Participants.SingleAsync(member => member.UserId == group.Member.Id && member.ChatId == group.Chat.Id);
            if (suspend) await host.SetStatusAsync(group.Member.Id, "SUSPENDED");
            else
            {
                using var removed = await host.SendAsAsync(HttpMethod.Delete, $"/api/chats/{group.Chat.Id}/members/{group.Member.Id}", group.OwnerToken);
                removed.EnsureSuccessStatusCode();
            }
            var rejected = false;
            try { await scope.ServiceProvider.GetRequiredService<IChatReadRepository>().AdvanceAsync(group.Chat.Id, group.Member.Id, sent.Id, default); }
            catch (UseCaseException exception) when (exception.Code == "forbidden") { rejected = true; }
            TestAssert.True(rejected, "Previously tracked membership/account state cannot authorize a new read marker.");
        }
    }

    private static async Task<MessageResponse[]> SendAsync(ApiTestHost host, long chatId, params long[] senders)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var messages = scope.ServiceProvider.GetRequiredService<MessageService>();
        var result = new List<MessageResponse>();
        foreach (var sender in senders) result.Add(await messages.SendAsync(chatId, sender, "Saved message", null, default));
        return result.ToArray();
    }

    private static async Task<ChatDetailsResponse> DetailsAsync(ApiTestHost host, long chatId, string token)
    {
        using var response = await host.SendAsAsync(HttpMethod.Get, $"/api/chats/{chatId}", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatDetailsResponse>())!;
    }

    private static async Task<ChatLastRead> StoredAsync(ApiTestHost host, long chatId, long userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        return await db.Set<ChatLastRead>().SingleAsync(read => db.Participants.Any(member => member.Id == read.ChatParticipantId && member.ChatId == chatId && member.UserId == userId));
    }

    private static Task<ChatUpdateResponse> UpdateAsync(SignalRTestClient client) => client.Updates.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    private static async Task RejectAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (HubException exception) when (exception.Message.Contains(code + ":", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected a stable hub error: " + code);
    }
}
