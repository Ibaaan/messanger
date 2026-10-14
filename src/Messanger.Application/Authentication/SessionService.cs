using Messanger.Application.Persistence;
using Messanger.Domain;

namespace Messanger.Application.Authentication;

public sealed class SessionService(AuthenticationService authentication, IRefreshSessionRepository sessions,
    IRefreshTokenCodec refreshTokens, IAccessTokenIssuer accessTokens, TimeProvider clock)
{
    public async Task<SessionResult> LoginAsync(string? username, string? password, CancellationToken ct)
    {
        var user = await authentication.AuthenticateAsync(username, password, ct);
        var deadline = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds());
        var refresh = refreshTokens.Issue(user.Id, user.SessionVersion, Guid.NewGuid(), deadline);
        await sessions.CreateAsync(user, Record(refresh), ct);
        return Result(user, refresh);
    }

    public async Task<RefreshAttempt> RefreshAsync(string? token, CancellationToken ct)
    {
        var credential = refreshTokens.Validate(token);
        if (credential is null) return new(null, null);
        var successor = refreshTokens.Issue(credential.UserId, credential.SessionVersion, credential.FamilyId, credential.ExpiresAt);
        var rotation = await sessions.RotateAsync(credential, Record(successor), ct);
        return new(rotation.User is null ? null : Result(rotation.User, successor),
            rotation.ReuseDetected ? new(credential.UserId, credential.FamilyId) : null);
    }

    public async Task<SessionIdentity?> LogoutAsync(string? token, CancellationToken ct)
    {
        // A signed expired token can still identify its family for idempotent logout.
        var credential = refreshTokens.Validate(token, validateLifetime: false);
        return credential is not null && await sessions.RevokeAsync(credential, ct)
            ? new(credential.UserId, credential.FamilyId) : null;
    }

    private SessionResult Result(User user, SignedRefreshToken refresh)
    {
        var access = accessTokens.Issue(user, refresh.Credential.FamilyId, refresh.Credential.ExpiresAt);
        return new(new(access.Value, "Bearer", access.ExpiresAt, UserResponse.From(user)), refresh.Value, refresh.Credential.ExpiresAt);
    }

    private RefreshTokenFamily Record(SignedRefreshToken token) => new()
    {
        UserId = token.Credential.UserId, FamilyId = token.Credential.FamilyId,
        JtiHash = token.Credential.JtiHash, IssuedAt = clock.GetUtcNow(), ExpiresAt = token.Credential.ExpiresAt
    };
}
