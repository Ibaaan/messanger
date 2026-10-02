using Messanger.Application;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(MessangerDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = username.TrimStart('@').ToUpperInvariant();
        return db.Users.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<UserSummary>> SearchAsync(string query, long excludedUserId, CancellationToken cancellationToken = default)
    {
        var normalized = query.ToUpperInvariant();
        return await (from user in db.Users.AsNoTracking()
                      join profile in db.Set<UserProfile>() on user.Id equals profile.UserId
                      where user.Id != excludedUserId && user.AccountStatus == "ACTIVE"
                          && user.NormalizedUsername.Contains(normalized)
                      orderby user.NormalizedUsername
                      select new UserSummary(user.Id, user.Username, profile.DisplayName))
            .Take(10).ToListAsync(cancellationToken);
    }

    public void Add(User user, UserProfile profile)
    {
        user.NormalizedUsername = user.Username.ToUpperInvariant();
        db.Users.Add(user);
        profile.User = user;
        db.Set<UserProfile>().Add(profile);
    }
}