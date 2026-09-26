using Messanger.Domain;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messanger.IntegrationTests.Repositories;

internal static class UserRepositoryTests
{
    public static async Task AddPersistsUserAndProfile(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        database.Context.ChangeTracker.Clear();

        var storedUser = await database.Users.GetByUsernameAsync(user.Username.ToUpperInvariant());
        var profileExists = await database.Context.Set<UserProfile>()
            .AnyAsync(profile => profile.UserId == user.Id);

        TestAssert.Equal(user.Id, storedUser?.Id, "Username lookup should ignore case.");
        TestAssert.True(profileExists, "The new profile should reference the persisted user.");
    }

    public static async Task DuplicateUsernameIsRejected(TestDatabase database)
    {
        var user = await database.CreateUserAsync();

        try
        {
            await database.CreateUserAsync(user.Username.ToUpperInvariant());
        }
        catch (DbUpdateException exception) when
            (exception.InnerException is PostgresException
             {
                 SqlState: PostgresErrorCodes.UniqueViolation,
                 ConstraintName: "IX_users_normalized_username"
             })
        {
            return;
        }

        throw new InvalidOperationException("PostgreSQL should reject duplicate normalized usernames.");
    }
}