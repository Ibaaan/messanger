using System.Text.RegularExpressions;
using Messanger.Application;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure;

public sealed class MessangerDbContext(DbContextOptions<MessangerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatParticipant> Participants => Set<ChatParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<RefreshTokenFamily> RefreshTokens => Set<RefreshTokenFamily>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().ToTable("users");
        model.Entity<UserProfile>().ToTable("user_profiles");
        model.Entity<Chat>().ToTable("chats");
        model.Entity<ChatParticipant>().ToTable("chat_participants");
        model.Entity<Message>().ToTable("messages");
        model.Entity<MessageMedia>().ToTable("message_media");
        model.Entity<ChatLastRead>().ToTable("chat_last_read");
        model.Entity<RefreshTokenFamily>().ToTable("refresh_token_families");
        var user = model.Entity<User>();
        user.Property(x => x.Username).HasMaxLength(20);
        user.Property(x => x.NormalizedUsername).HasMaxLength(20);
        user.Property(x => x.Language).HasMaxLength(5);
        user.Property(x => x.Role).HasMaxLength(20);
        user.Property(x => x.AccountStatus).HasMaxLength(20);
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        user.ToTable(t =>
            t.HasCheckConstraint("ck_users_status", "account_status IN ('ACTIVE','SUSPENDED','DELETED')"));
        var profile = model.Entity<UserProfile>();
        profile.Property(x => x.DisplayName).HasMaxLength(120);
        profile.Property(x => x.Bio).HasMaxLength(2000);
        profile.HasIndex(x => x.UserId).IsUnique();
        profile.HasOne(x => x.User).WithOne().HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        var chat = model.Entity<Chat>();
        chat.Property(x => x.ChatType).HasMaxLength(10);
        chat.Property(x => x.Name).HasMaxLength(120);
        chat.Property(x => x.Description).HasMaxLength(2000);
        chat.ToTable(t => t.HasCheckConstraint("ck_chats_type", "chat_type IN ('group','direct')"));
        var participant = model.Entity<ChatParticipant>();
        participant.Property(x => x.Role).HasMaxLength(20);
        participant.HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Restrict);
        participant.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        participant.HasIndex(x => new { x.ChatId, x.UserId }).IsUnique();
        participant.HasIndex(x => new { x.UserId, x.ChatId });
        participant.HasIndex(x => new { x.ChatId, x.UserId }, "ix_active_participants").HasFilter("left_at IS NULL");
        var message = model.Entity<Message>();
        message.HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Restrict);
        message.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        message.HasOne<Message>().WithMany().HasForeignKey(x => x.ReplyToMessageId).OnDelete(DeleteBehavior.Restrict);
        message.HasIndex(x => new { x.ChatId, x.Id });
        message.Property(x => x.MessageType).HasMaxLength(10);
        message.ToTable(t =>
            t.HasCheckConstraint("ck_messages_type", "message_type IN ('TEXT','IMAGE','VIDEO','MIXED')"));
        var media = model.Entity<MessageMedia>();
        media.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        media.HasIndex(x => new { x.MessageId, x.MediaOrder }).IsUnique();
        media.Property(x => x.MediaType).HasMaxLength(10);
        media.Property(x => x.ContentType).HasMaxLength(100);
        media.Property(x => x.ChecksumSha256).HasColumnType("char(64)");
        media.ToTable(t => t.HasCheckConstraint("ck_media_type", "media_type IN ('photo','video')"));
        var read = model.Entity<ChatLastRead>();
        read.HasOne<ChatParticipant>().WithOne().HasForeignKey<ChatLastRead>(x => x.ChatParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
        read.HasOne<Message>().WithMany().HasForeignKey(x => x.LastReadMessageId).OnDelete(DeleteBehavior.Restrict);
        read.HasIndex(x => x.ChatParticipantId).IsUnique();
        var token = model.Entity<RefreshTokenFamily>();
        token.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        token.Property(x => x.JtiHash).HasColumnType("char(64)");
        token.Property(x => x.ReplacedByJtiHash).HasColumnType("char(64)");
        token.HasIndex(x => x.JtiHash).IsUnique();
        token.HasIndex(x => new { x.FamilyId, x.RevokedAt });
        token.HasIndex(x => new { x.UserId, x.ExpiresAt });
        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}