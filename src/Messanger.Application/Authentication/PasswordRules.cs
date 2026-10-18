namespace Messanger.Application.Authentication;

public static class PasswordRules
{
    public static string Validate(string? password)
    {
        if (password is null || password.Length is < 10 or > 256 || string.IsNullOrWhiteSpace(password))
            throw new UseCaseException("validation_failed", "Password must contain 10–256 characters and cannot be all whitespace.");
        return password;
    }
}
