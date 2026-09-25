using Messanger.Domain;

namespace Messanger.Application;

public interface IRefreshTokenFamilyRepository
{
    Task<RefreshTokenFamily?> GetByHashAsync(string jtiHash, CancellationToken cancellationToken = default);
    void Add(RefreshTokenFamily token);
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);
    Task RevokeForUserAsync(long userId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);
}