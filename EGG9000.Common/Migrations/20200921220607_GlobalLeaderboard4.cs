using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class GlobalLeaderboard4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<bool>(
                name: "CheckFailed",
                table: "GlobalLeaderboardCoops",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckFailed",
                table: "GlobalLeaderboardCoops");


        }
    }
}
