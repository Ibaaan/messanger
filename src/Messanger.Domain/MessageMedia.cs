namespace Messanger.Domain;

public sealed class MessageMedia
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public required string MediaType { get; set; }
    public required string ObjectKey { get; set; }
    public int MediaOrder { get; set; }
    public required string ContentType { get; set; }
    public long ByteLength { get; set; }
    public required string ChecksumSha256 { get; set; }
}