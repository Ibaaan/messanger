namespace Messanger.Domain;

public sealed class Message
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public long? ReplyToMessageId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsEdited { get; set; }
    public string MessageType { get; set; } = "TEXT";
}