using Messanger.Domain;

namespace Messanger.Application;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    void Add(User user, UserProfile profile);
}