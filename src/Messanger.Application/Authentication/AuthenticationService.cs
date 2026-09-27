using System.Text.RegularExpressions;
using Messanger.Application.Persistence;
using Messanger.Domain;

namespace Messanger.Application.Authentication;

public sealed partial class AuthenticationService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    IAccessTokenIssuer tokens)
{
    public async Task<UserResponse> RegisterAsync(string? username, string? password,
        CancellationToken cancellationToken)
    {
        if (!IsValidUsername(username))
            throw new UseCaseException("validation_failed",
                "Username must contain 3–20 ASCII letters, digits, or underscores.");
        if (password is null || password.Length is < 10 or > 256 || string.IsNullOrWhiteSpace(password))
            throw new UseCaseException("validation_failed",
                "Password must contain 10–256 characters and cannot be all whitespace.");
        if (await users.GetByUsernameAsync(username!, cancellationToken) is not null)
            throw new UseCaseException("username_taken", "Username is already taken.");

        var user = new User
        {
            Username = username!,
            NormalizedUsername = username!.ToUpperInvariant(),
            PasswordHash = "",
            Language = "ru"
        };
        user.PasswordHash = passwords.Hash(user, password);
        users.Add(user, new UserProfile { DisplayName = username! });
        // One save atomically inserts both the user and their default profile.
        // The database unique index also protects concurrent registrations.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserResponse.From(user);
    }

    public async Task<LoginResponse> LoginAsync(string? username, string? password, CancellationToken cancellationToken)
    {
        if (!IsValidUsername(username) || password is null || password.Length is < 10 or > 256)
            throw InvalidCredentials();

        var user = await users.GetByUsernameAsync(username!, cancellationToken);
        // Unknown users still perform a real hash verification to reduce timing differences.
        var validPassword = passwords.Verify(user, password);
        if (!validPassword || user is null || user.AccountStatus != "ACTIVE")
            throw InvalidCredentials();

        var token = tokens.Issue(user);
        return new LoginResponse(token.Value, "Bearer", token.ExpiresAt, UserResponse.From(user));
    }

    private static bool IsValidUsername(string? username)
    {
        return username is not null && UsernamePattern().IsMatch(username);
    }

    private static UseCaseException InvalidCredentials()
    {
        return new UseCaseException("invalid_credentials", "Invalid username or password.");
    }

    [GeneratedRegex("\\A[A-Za-z0-9_]{3,20}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}