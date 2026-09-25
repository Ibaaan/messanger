namespace Messanger.Domain;

public sealed class ChatParticipant
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }
    public string Role { get; set; } = "MEMBER";
}