using Messanger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messanger.Infrastructure.Persistence.Configurations;

internal sealed class ChatLastReadConfiguration : IEntityTypeConfiguration<ChatLastRead>
{
    public void Configure(EntityTypeBuilder<ChatLastRead> builder)
    {
        builder.ToTable("chat_last_read");
        builder.HasOne<ChatParticipant>().WithOne().HasForeignKey<ChatLastRead>(x => x.ChatParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>().WithMany().HasForeignKey(x => x.LastReadMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ChatParticipantId).IsUnique();
    }
}
