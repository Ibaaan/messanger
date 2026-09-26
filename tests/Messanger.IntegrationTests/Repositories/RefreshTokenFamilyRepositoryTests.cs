using Messanger.Domain;
using Messanger.IntegrationTests.Support;

namespace Messanger.IntegrationTests.Repositories;

internal static class RefreshTokenFamilyRepositoryTests
{
    public static async Task RevokeFamilyPersistsRevocation(TestDatabase database)
    {
        var user = await database.CreateUserAsync();
        var token = new RefreshTokenFamily
        {
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            JtiHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        database.Tokens.Add(token);
        await database.SaveChangesAsync();

        await database.Tokens.RevokeFamilyAsync(token.FamilyId, DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var storedToken = await database.Tokens.GetByHashAsync(token.JtiHash);
        TestAssert.True(storedToken?.RevokedAt != null, "Family revocation should be persisted in PostgreSQL.");
    }
}