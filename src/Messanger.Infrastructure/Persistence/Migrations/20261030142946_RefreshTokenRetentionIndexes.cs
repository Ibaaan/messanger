using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messanger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_families_expires_at_id",
                table: "refresh_token_families",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_families_family_id_expires_at",
                table: "refresh_token_families",
                columns: new[] { "family_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_token_families_expires_at_id",
                table: "refresh_token_families");

            migrationBuilder.DropIndex(
                name: "IX_refresh_token_families_family_id_expires_at",
                table: "refresh_token_families");
        }
    }
}
