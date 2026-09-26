using Messanger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messanger.Infrastructure.Persistence.Configurations;

internal sealed class MessageMediaConfiguration : IEntityTypeConfiguration<MessageMedia>
{
    public void Configure(EntityTypeBuilder<MessageMedia> builder)
    {
        builder.ToTable("message_media");
        builder.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.MessageId, x.MediaOrder }).IsUnique();
        builder.Property(x => x.MediaType).HasMaxLength(10);
        builder.Property(x => x.ContentType).HasMaxLength(100);
        builder.Property(x => x.ChecksumSha256).HasColumnType("char(64)");
        builder.ToTable(t => t.HasCheckConstraint("ck_media_type", "media_type IN ('photo','video')"));
    }
}