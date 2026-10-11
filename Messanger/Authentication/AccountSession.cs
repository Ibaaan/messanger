using System.Globalization;
using System.Security.Claims;
using Messanger.Domain;

namespace Messanger.Authentication;

internal static class AccountSession
{
    public static bool IsCurrent(User? user, ClaimsPrincipal principal) =>
        user is { AccountStatus: "ACTIVE" }
        && int.TryParse(principal.FindFirst("session_version")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version == user.SessionVersion;
}
