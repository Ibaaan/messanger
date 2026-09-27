using Messanger.Application.Authentication;
using Messanger.Domain;
using Microsoft.AspNetCore.Identity;

namespace Messanger.Infrastructure.Authentication;

internal sealed class IdentityPasswordService : IPasswordService
{
    private readonly string dummyHash;
    private readonly User dummyUser = new() { Username = "dummy", NormalizedUsername = "DUMMY", PasswordHash = "" };
    private readonly PasswordHasher<User> hasher = new();

    public IdentityPasswordService()
    {
        dummyHash = hasher.HashPassword(dummyUser, Guid.NewGuid().ToString("N"));
    }

    public string Hash(User user, string password)
    {
        return hasher.HashPassword(user, password);
    }

    public bool Verify(User? user, string password)
    {
        var result = hasher.VerifyHashedPassword(user ?? dummyUser, user?.PasswordHash ?? dummyHash, password);
        return user is not null && result != PasswordVerificationResult.Failed;
    }
}