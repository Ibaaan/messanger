using Messanger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messanger.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(x => x.Username).HasMaxLength(20);
        builder.Property(x => x.NormalizedUsername).HasMaxLength(20);
        builder.Property(x => x.Language).HasMaxLength(5);
        builder.Property(x => x.Role).HasMaxLength(20);
        builder.Property(x => x.AccountStatus).HasMaxLength(20);
        builder.HasIndex(x => x.NormalizedUsername).IsUnique();
        builder.ToTable(t =>
            t.HasCheckConstraint("ck_users_status", "account_status IN ('ACTIVE','SUSPENDED','DELETED')"));
    }
}