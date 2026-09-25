namespace Messanger.Domain;

public sealed class UserProfile
{
    public long Id { get; set; }
    public User User { get; set; } = null!;
    public long UserId { get; set; }
    public required string DisplayName { get; set; }
    public string Bio { get; set; } = "";
    public DateOnly? BirthDate { get; set; }
    public string? AvatarObjectKey { get; set; }
}