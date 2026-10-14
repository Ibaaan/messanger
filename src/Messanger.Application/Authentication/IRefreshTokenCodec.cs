namespace Messanger.Application.Authentication;

public interface IRefreshTokenCodec
{
    SignedRefreshToken Issue(long userId, int sessionVersion, Guid familyId, DateTimeOffset expiresAt);
    RefreshCredential? Validate(string? token, bool validateLifetime = true);
}
