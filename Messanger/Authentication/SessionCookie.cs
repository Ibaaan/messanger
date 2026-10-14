using Messanger.Application.Authentication;

namespace Messanger.Authentication;

internal static class SessionCookie
{
    public const string Name = "__Secure-MessangerRefresh";
    private static CookieOptions Options() => new()
    {
        Path = "/auth", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true
    };

    public static void Write(HttpContext context, SessionResult session, TimeProvider clock)
    {
        var options = Options();
        options.Expires = session.ExpiresAt;
        options.MaxAge = session.ExpiresAt - clock.GetUtcNow();
        context.Response.Cookies.Append(Name, session.RefreshToken, options);
    }

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(Name, Options());
}
