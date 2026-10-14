using Messanger.Application.Authentication;
using Messanger.Domain;

namespace Messanger.Application.Persistence;

public interface IRefreshSessionRepository
{
    Task CreateAsync(User user, RefreshTokenFamily token, CancellationToken ct);
    Task<RefreshRotation> RotateAsync(RefreshCredential credential, RefreshTokenFamily successor, CancellationToken ct);
    Task<bool> RevokeAsync(RefreshCredential credential, CancellationToken ct);
    Task<bool> IsActiveAsync(long userId, Guid familyId, CancellationToken ct);
}
