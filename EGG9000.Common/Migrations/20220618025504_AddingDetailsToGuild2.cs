using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class AddingDetailsToGuild2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelWarningMessageForUser",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "EliteCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "FailedCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "GameEventsChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "GeneralChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "LeaderboardChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "RulesChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "StandardCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "WelcomeChannel",
                table: "Guilds");

            migrationBuilder.AddColumn<string>(
                name: "CoopCategories",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinishedCategories",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "_channelDetailsJson",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoopCategories",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "FinishedCategories",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "_channelDetailsJson",
                table: "Guilds");

            migrationBuilder.AddColumn<decimal>(
                name: "ChannelWarningMessageForUser",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EliteCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FailedCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GameEventsChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GeneralChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LeaderboardChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RulesChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WelcomeChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);
        }
    }
}
