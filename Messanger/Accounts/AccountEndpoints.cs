using System.Globalization;
using System.Security.Claims;
using Messanger.Authentication;
using Messanger.Application.Accounts;
using Messanger.Chats;
using Messanger.Http;
using Messanger.Realtime;
using Messanger.Security;

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
        endpoints.MapPost("/api/users/me/password", async (ChangePasswordRequest request, ClaimsPrincipal principal,
            PasswordChangeService passwords, AccountConnections connections, HttpContext context, SecurityAudit audit, CancellationToken ct) =>
        {
            if (!context.Request.IsHttps)
            {
                await ApiError.WriteAsync(context, 400, "https_required", "Use HTTPS to change your password.");
                return Results.Empty;
            }
            var userId = ChatEndpoints.UserId(principal);
            var version = int.Parse(principal.FindFirst("session_version")!.Value, CultureInfo.InvariantCulture);
            var newVersion = await passwords.ChangeAsync(userId, version, request.CurrentPassword, request.NewPassword, ct);
            connections.RevokeBefore(userId, newVersion);
            audit.Changed(context, "password_changed", userId);
            SessionCookie.Clear(context);
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<PasswordAttemptFilter>().WithTags("Accounts").WithName("ChangeMyPassword")
            .WithDescription("HTTPS, active access Bearer, and current password required. New password follows registration policy. Atomically replaces the hash and revokes every existing session; log in again after 204.")
            .Produces(StatusCodes.Status204NoContent).WithApiErrors();
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
