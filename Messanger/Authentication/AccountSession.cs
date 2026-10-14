using System.Globalization;
using System.Security.Claims;
using Messanger.Application.Persistence;

namespace Messanger.Authentication;

internal sealed class AccountSession(IUserRepository users, IRefreshSessionRepository sessions)
{
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!long.TryParse(principal.FindFirst("sub")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !int.TryParse(principal.FindFirst("session_version")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var familyId)) return false;
        var user = await users.GetByIdAsync(id, ct);
        return user is { AccountStatus: "ACTIVE" } && user.SessionVersion == version
            && await sessions.IsActiveAsync(id, familyId, ct);
    }
}
