using Messanger.Domain;

namespace Messanger.Application.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSummary>> SearchAsync(string query, long excludedUserId, CancellationToken cancellationToken = default);
    void Add(User user, UserProfile profile);
}