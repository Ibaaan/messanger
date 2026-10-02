using System.Globalization;
using System.Security.Claims;
using Messanger.Application.Chats;
using Messanger.Application.Messages;

namespace Messanger.Chats;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/search", async (string? query, ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.SearchUsersAsync(UserId(user), query, ct))).RequireAuthorization();
        endpoints.MapGet("/api/chats", async (ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.ListAsync(UserId(user), ct))).RequireAuthorization();
        endpoints.MapPost("/api/chats/create", async (CreateChatRequest request, ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
        {
            var chat = await chats.CreateAsync(UserId(user), request.ChatName, request.Description, request.UserIds, ct);
            return Results.Created($"/api/chats/{chat.Id}", chat);
        }).RequireAuthorization();
        endpoints.MapGet("/api/chats/{chatId:long}", async (long chatId, ClaimsPrincipal user, ChatService chats, CancellationToken ct) =>
            Results.Ok(await chats.GetAsync(chatId, UserId(user), ct))).RequireAuthorization();
        endpoints.MapGet("/api/chats/{chatId:long}/messages", async (long chatId, long? beforeMessageId, int? limit,
            ClaimsPrincipal user, MessageService messages, CancellationToken ct) =>
            Results.Ok(await messages.HistoryAsync(chatId, UserId(user), beforeMessageId, limit ?? 50, ct))).RequireAuthorization();
        return endpoints;
    }

    internal static long UserId(ClaimsPrincipal user) => long.Parse(user.FindFirst("sub")!.Value, CultureInfo.InvariantCulture);
}
