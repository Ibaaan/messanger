using System.Security.Claims;
using Messanger.Application.Accounts;
using Messanger.Chats;
using Messanger.Http;
using Messanger.Realtime;

namespace Messanger.Accounts;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/admin/users/{userId:long}/status", async (long userId, ChangeAccountStatusRequest request,
            ClaimsPrincipal principal, AccountService accounts, AccountConnections connections, CancellationToken ct) =>
        {
            var response = await accounts.ChangeStatusAsync(ChatEndpoints.UserId(principal), userId, request.AccountStatus, ct);
            connections.RevokeBefore(userId, response.SessionVersion);
            return Results.Ok(response.Account);
        }).RequireAuthorization().WithTags("Accounts").WithName("ChangeAccountStatus")
            .WithDescription("Current database ADMIN role required. ACTIVE or SUSPENDED; cannot target yourself or restore DELETED accounts.")
            .Produces<AccountResponse>().WithApiErrors();
        endpoints.MapDelete("/api/users/me", async (ClaimsPrincipal principal, AccountService accounts,
            AccountConnections connections, CancellationToken ct) =>
        {
            var userId = ChatEndpoints.UserId(principal);
            await accounts.DeleteSelfAsync(userId, ct);
            connections.Revoke(userId);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Accounts").WithName("DeleteMyAccount")
            .WithDescription("Soft-deletes the current account and revokes sessions. Transfer group ownership first. Messages remain as deleted-account history.")
            .Produces(StatusCodes.Status204NoContent).WithApiErrors();
        return endpoints;
    }
}
