using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class GlobalLeaderboard2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBackup",
                table: "GlobalLeaderboardUsers",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "GlobalLeaderboardUsers",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_name",
                table: "GlobalLeaderboardUsers",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBackup",
                table: "GlobalLeaderboardUsers");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "GlobalLeaderboardUsers");

            migrationBuilder.DropColumn(
                name: "user_name",
                table: "GlobalLeaderboardUsers");
        }
    }
}
