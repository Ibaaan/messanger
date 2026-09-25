namespace Messanger.Domain;

public sealed class User
{
    public long Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Language { get; set; } = "en";
    public string Role { get; set; } = "USER";
    public string AccountStatus { get; set; } = "ACTIVE";
}