using System.Net;
using System.Net.Http.Json;
using Messanger.Application.Chats;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class ChatMembershipApiTests
{
    public static async Task OwnerAddsAndRejoinsWithoutDuplicatingMembership(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var denied = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.MemberToken, new { userId = group.Other.Id });
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        using var promoted = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Member.Id}/role"), group.OwnerToken, new { role = "MODERATOR" });
        promoted.EnsureSuccessStatusCode();
        using var removed = await host.SendAsAsync(HttpMethod.Delete, Path(group, $"/members/{group.Member.Id}"), group.OwnerToken);
        TestAssert.Equal(HttpStatusCode.NoContent, removed.StatusCode, "Owner removal should succeed.");
        using var rejoined = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = group.Member.Id, role = "OWNER" });
        rejoined.EnsureSuccessStatusCode();
        var chat = (await rejoined.Content.ReadFromJsonAsync<ChatResponse>())!;
        TestAssert.Equal("MEMBER", chat.Participants.Single(item => item.UserId == group.Member.Id).Role, "Rejoin must reset old privileges and ignore caller-supplied role.");
        using var duplicate = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = group.Member.Id });
        await ApiAssertions.ErrorAsync(duplicate, HttpStatusCode.Conflict, "member_already_active");
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        TestAssert.Equal(1, await db.Participants.CountAsync(item => item.ChatId == group.Chat.Id && item.UserId == group.Member.Id), "Rejoin should reuse the unique membership row.");
    }

    public static async Task ModeratorsCanRemoveOnlyOrdinaryMembers(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var add = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = group.Other.Id });
        add.EnsureSuccessStatusCode();
        using var promote = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Member.Id}/role"), group.OwnerToken, new { role = "MODERATOR" });
        promote.EnsureSuccessStatusCode();
        using var ownerDenied = await host.SendAsAsync(HttpMethod.Delete, Path(group, $"/members/{group.Owner.Id}"), group.MemberToken);
        await ApiAssertions.ErrorAsync(ownerDenied, HttpStatusCode.Conflict, "owner_transfer_required");
        using var roleDenied = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Other.Id}/role"), group.MemberToken, new { role = "MODERATOR" });
        await ApiAssertions.ErrorAsync(roleDenied, HttpStatusCode.Forbidden, "forbidden");
        using var promoteOther = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Other.Id}/role"), group.OwnerToken, new { role = "MODERATOR" });
        promoteOther.EnsureSuccessStatusCode();
        using var peerDenied = await host.SendAsAsync(HttpMethod.Delete, Path(group, $"/members/{group.Other.Id}"), group.MemberToken);
        await ApiAssertions.ErrorAsync(peerDenied, HttpStatusCode.Forbidden, "forbidden");
        using var demote = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Other.Id}/role"), group.OwnerToken, new { role = "MEMBER" });
        demote.EnsureSuccessStatusCode();
        using var allowed = await host.SendAsAsync(HttpMethod.Delete, Path(group, $"/members/{group.Other.Id}"), group.MemberToken);
        TestAssert.Equal(HttpStatusCode.NoContent, allowed.StatusCode, "A moderator may remove an ordinary member.");
        using var history = await host.SendAsAsync(HttpMethod.Get, Path(group, "/messages"), group.OtherToken);
        await ApiAssertions.ErrorAsync(history, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task OwnerTransfersBeforeLeaving(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var blocked = await host.SendAsAsync(HttpMethod.Post, Path(group, "/leave"), group.OwnerToken);
        await ApiAssertions.ErrorAsync(blocked, HttpStatusCode.Conflict, "owner_transfer_required");
        using var notMember = await host.SendAsAsync(HttpMethod.Post, Path(group, "/ownership"), group.OwnerToken, new { userId = group.Other.Id });
        await ApiAssertions.ErrorAsync(notMember, HttpStatusCode.NotFound, "not_found");
        using var transfer = await host.SendAsAsync(HttpMethod.Post, Path(group, "/ownership"), group.OwnerToken, new { userId = group.Member.Id });
        transfer.EnsureSuccessStatusCode();
        var chat = (await transfer.Content.ReadFromJsonAsync<ChatResponse>())!;
        TestAssert.Equal(group.Member.Id, chat.Participants.Single(item => item.Role == "OWNER").UserId, "There should be exactly one new owner.");
        TestAssert.Equal("MEMBER", chat.Participants.Single(item => item.UserId == group.Owner.Id).Role, "The former owner becomes an ordinary member.");
        using var leave = await host.SendAsAsync(HttpMethod.Post, Path(group, "/leave"), group.OwnerToken);
        TestAssert.Equal(HttpStatusCode.NoContent, leave.StatusCode, "The former owner may leave after transfer.");
        using var denied = await host.SendAsAsync(HttpMethod.Get, Path(group), group.OwnerToken);
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task ConcurrentTransfersKeepExactlyOneOwner(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var add = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = group.Other.Id });
        add.EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(new[] { group.Member.Id, group.Other.Id }.Select(id =>
            host.SendAsAsync(HttpMethod.Post, Path(group, "/ownership"), group.OwnerToken, new { userId = id })));
        try
        {
            TestAssert.Equal(1, responses.Count(item => item.StatusCode == HttpStatusCode.OK), "Only one competing transfer should succeed.");
            TestAssert.Equal(1, responses.Count(item => item.StatusCode == HttpStatusCode.Forbidden), "The former owner must lose management authority.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        TestAssert.Equal(1, await db.Participants.CountAsync(item => item.ChatId == group.Chat.Id && item.LeftAt == null && item.Role == "OWNER"), "Concurrent transfers must preserve one active owner.");
    }

    public static async Task MembershipContractsRejectInvalidInput(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var missingUser = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { });
        await ApiAssertions.ErrorAsync(missingUser, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var invalidRole = await host.SendAsAsync(HttpMethod.Patch, Path(group, $"/members/{group.Member.Id}/role"), group.OwnerToken, new { role = "OWNER" });
        await ApiAssertions.ErrorAsync(invalidRole, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var invalidId = await host.SendAsAsync(HttpMethod.Get, "/api/chats/0", group.OwnerToken);
        await ApiAssertions.ErrorAsync(invalidId, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var blankName = await host.SendAsAsync(HttpMethod.Patch, Path(group), group.OwnerToken, new { chatName = "  " });
        await ApiAssertions.ErrorAsync(blankName, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var denied = await host.SendAsAsync(HttpMethod.Patch, Path(group), group.MemberToken, new { chatName = "Not allowed" });
        await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        using var updated = await host.SendAsAsync(HttpMethod.Patch, Path(group), group.OwnerToken, new { chatName = "  Updated group  ", description = "Details" });
        updated.EnsureSuccessStatusCode();
        TestAssert.Equal("Updated group", (await updated.Content.ReadFromJsonAsync<ChatResponse>())!.Name, "Group names should be validated and trimmed.");
    }

    public static async Task LeaveIsIdempotentAndInactiveTargetsAreRejected(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await host.SetStatusAsync(group.Other.Id, "SUSPENDED");
        using var inactive = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = group.Other.Id });
        await ApiAssertions.ErrorAsync(inactive, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var unknown = await host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = long.MaxValue });
        await ApiAssertions.ErrorAsync(unknown, HttpStatusCode.UnprocessableEntity, "validation_failed");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var left = await host.SendAsAsync(HttpMethod.Post, Path(group, "/leave"), group.MemberToken);
            TestAssert.Equal(HttpStatusCode.NoContent, left.StatusCode, "Repeated self-leave should be harmless.");
        }
        using var list = await host.SendAsAsync(HttpMethod.Get, "/api/chats", group.MemberToken);
        TestAssert.Equal(0, (await list.Content.ReadFromJsonAsync<ChatResponse[]>())!.Length, "Departed groups should disappear from the caller's list.");
    }

    public static async Task ConcurrentAddsRespectTheMemberLimit(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var candidates = new List<User>();
        for (var index = 0; index < 50; index++)
        {
            var username = ApiTestHost.NewUsername();
            var user = new User { Username = username, NormalizedUsername = username.ToUpperInvariant(), PasswordHash = "unused-test-hash" };
            db.Users.Add(user);
            db.Set<UserProfile>().Add(new UserProfile { User = user, DisplayName = username });
            candidates.Add(user);
        }
        await db.SaveChangesAsync();
        foreach (var user in candidates.Take(48)) db.Participants.Add(new ChatParticipant { ChatId = group.Chat.Id, UserId = user.Id });
        await db.SaveChangesAsync();
        var responses = await Task.WhenAll(candidates.Skip(48).Select(user =>
            host.SendAsAsync(HttpMethod.Post, Path(group, "/members"), group.OwnerToken, new { userId = user.Id })));
        try
        {
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK), "Only the last available slot should be filled.");
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict), "The competing addition should see the capacity conflict.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
        TestAssert.Equal(51, await db.Participants.CountAsync(item => item.ChatId == group.Chat.Id && item.LeftAt == null), "Concurrent additions must not exceed the member limit.");
    }

    private static string Path(ChatTestGroup group, string suffix = "") => $"/api/chats/{group.Chat.Id}" + suffix;
}
