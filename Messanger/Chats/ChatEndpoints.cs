using System.Globalization;
using System.Security.Claims;
using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Http;
using Messanger.Application.Messages;
using Messanger.Realtime;

namespace Messanger.Chats;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/search", async (string? query, ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.SearchUsersAsync(UserId(user), query, ct))).RequireAuthorization().WithTags("Users").WithName("SearchUsers").Produces<UserSummary[]>().WithApiErrors();
        endpoints.MapGet("/api/chats", async (ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.ListAsync(UserId(user), ct))).RequireAuthorization().WithTags("Chats").WithName("ListChats").Produces<ChatResponse[]>().WithApiErrors();
        endpoints.MapPost("/api/chats/create", async (CreateChatRequest request, ClaimsPrincipal user, ChatService chats, ChatBroadcaster broadcaster, CancellationToken ct) =>
        {
            var chat = await chats.CreateAsync(UserId(user), request.ChatName, request.Description, request.UserIds, ct);
            await broadcaster.CreatedAsync(chat, ct);
            return Results.Created($"/api/chats/{chat.Id}", chat);
        }).RequireAuthorization().WithTags("Chats").WithName("CreateChat").Produces<ChatResponse>(StatusCodes.Status201Created).WithApiErrors();
        endpoints.MapGet("/api/chats/{chatId:long}", async (long chatId, ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.GetAsync(chatId, UserId(user), ct))).RequireAuthorization().WithTags("Chats").WithName("GetChat").Produces<ChatDetailsResponse>().WithApiErrors();
        endpoints.MapGet("/api/chats/{chatId:long}/messages", async (long chatId, long? beforeMessageId, int? limit,
            ClaimsPrincipal user, MessageService messages, CancellationToken ct) =>
            Results.Ok(await messages.HistoryAsync(chatId, UserId(user), beforeMessageId, limit ?? 50, ct))).RequireAuthorization().WithTags("Messages").WithName("GetMessageHistory").WithDescription("Active membership required; positive exclusive beforeMessageId, limit 1–50; results are chronological.").Produces<MessageResponse[]>().WithApiErrors();
        return endpoints;
    }

    internal static long UserId(ClaimsPrincipal user) => long.Parse(user.FindFirst("sub")!.Value, CultureInfo.InvariantCulture);
}
