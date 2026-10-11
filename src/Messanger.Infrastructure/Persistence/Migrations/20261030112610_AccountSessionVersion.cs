using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messanger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "session_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "session_version",
                table: "users");
        }
    }
}
