namespace Messanger.Domain;

public sealed class ChatLastRead
{
    public long Id { get; set; }
    public long ChatParticipantId { get; set; }
    public long? LastReadMessageId { get; set; }
    public DateTimeOffset LastReadAt { get; set; } = DateTimeOffset.UtcNow;
}