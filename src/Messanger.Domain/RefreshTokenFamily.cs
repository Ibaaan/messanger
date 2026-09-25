namespace Messanger.Domain;

public sealed class RefreshTokenFamily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserId { get; set; }
    public Guid FamilyId { get; set; }
    public required string JtiHash { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByJtiHash { get; set; }
}