using Messanger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messanger.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenFamilyConfiguration : IEntityTypeConfiguration<RefreshTokenFamily>
{
    public void Configure(EntityTypeBuilder<RefreshTokenFamily> builder)
    {
        builder.ToTable("refresh_token_families");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.JtiHash).HasColumnType("char(64)");
        builder.Property(x => x.ReplacedByJtiHash).HasColumnType("char(64)");
        builder.HasIndex(x => x.JtiHash).IsUnique();
        builder.HasIndex(x => new { x.FamilyId, x.RevokedAt });
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });
        builder.HasIndex(x => new { x.ExpiresAt, x.Id });
        builder.HasIndex(x => new { x.FamilyId, x.ExpiresAt });
    }
}