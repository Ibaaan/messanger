namespace Messanger.Application.Authentication;

// Transport-internal result: only Login is returned as JSON. RefreshToken goes exclusively into a cookie.
public sealed record SessionResult(LoginResponse Login, string RefreshToken, DateTimeOffset ExpiresAt);
