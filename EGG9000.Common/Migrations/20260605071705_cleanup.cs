using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class Cleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveElites",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "ActiveStandards",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "InactiveElites",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "InactiveStandards",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "_faqTopicsJson",
                table: "Guilds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveElites",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveStandards",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InactiveElites",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InactiveStandards",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "_faqTopicsJson",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
