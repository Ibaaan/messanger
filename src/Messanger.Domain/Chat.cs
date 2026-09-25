namespace Messanger.Domain;

public sealed class Chat
{
    public long Id { get; set; }
    public string ChatType { get; set; } = "group";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ImageObjectKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}