using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class OverflowServer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscordSeverId",
                table: "Guilds",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OverflowServersJson",
                table: "Guilds",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OverflowGuildId",
                table: "Coops",
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscordSeverId",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "OverflowServersJson",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "OverflowGuildId",
                table: "Coops");
        }
    }
}
