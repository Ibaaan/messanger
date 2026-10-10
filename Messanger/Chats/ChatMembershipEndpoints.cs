using System.Security.Claims;
using Messanger.Application.Chats;
using Messanger.Realtime;

namespace Messanger.Chats;

internal static class ChatMembershipEndpoints
{
    public static IEndpointRouteBuilder MapChatMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chats/{chatId:long}").RequireAuthorization().WithTags("Chat membership");
        group.MapPost("/members", async (long chatId, AddChatMemberRequest request, ClaimsPrincipal user,
            ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var result = await membership.AddAsync(chatId, ChatEndpoints.UserId(user), request.UserId, ct);
            await broadcaster.MembershipChangedAsync(chatId, null, ct);
            return Results.Ok(result);
        })
            .WithName("AddChatMember").Produces<ChatResponse>();
        group.MapDelete("/members/{userId:long}", async (long chatId, long userId, ClaimsPrincipal user,
            ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            await membership.RemoveAsync(chatId, ChatEndpoints.UserId(user), userId, ct);
            await broadcaster.MembershipChangedAsync(chatId, userId, ct);
            return Results.NoContent();
        }).WithName("RemoveChatMember").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/leave", async (long chatId, ClaimsPrincipal user, ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var callerId = ChatEndpoints.UserId(user);
            await membership.LeaveAsync(chatId, callerId, ct);
            await broadcaster.MembershipChangedAsync(chatId, callerId, ct);
            return Results.NoContent();
        }).WithName("LeaveChat").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/ownership", async (long chatId, TransferChatOwnershipRequest request, ClaimsPrincipal user,
            ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var result = await membership.TransferAsync(chatId, ChatEndpoints.UserId(user), request.UserId, ct);
            await broadcaster.MembershipChangedAsync(chatId, null, ct);
            return Results.Ok(result);
        })
            .WithName("TransferChatOwnership").Produces<ChatResponse>();
        group.MapPatch("/members/{userId:long}/role", async (long chatId, long userId, ChangeChatMemberRoleRequest request,
            ClaimsPrincipal user, ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var result = await membership.ChangeRoleAsync(chatId, ChatEndpoints.UserId(user), userId, request.Role, ct);
            await broadcaster.MembershipChangedAsync(chatId, null, ct);
            return Results.Ok(result);
        })
            .WithName("ChangeChatMemberRole").Produces<ChatResponse>();
        group.MapPatch("", async (long chatId, UpdateChatRequest request, ClaimsPrincipal user,
            ChatMembershipService membership, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var result = await membership.UpdateAsync(chatId, ChatEndpoints.UserId(user), request.ChatName, request.Description, ct);
            await broadcaster.MembershipChangedAsync(chatId, null, ct);
            return Results.Ok(result);
        })
            .WithName("UpdateChatDetails").Produces<ChatResponse>();
        return endpoints;
    }
}
