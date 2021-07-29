using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class GlobalLeaderboard6 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<int>(
                name: "DegreeOfSeperation",
                table: "GlobalLeaderboardUsers",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DegreeOfSeperation",
                table: "GlobalLeaderboardCoops",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DegreeOfSeperation",
                table: "GlobalLeaderboardUsers");

            migrationBuilder.DropColumn(
                name: "DegreeOfSeperation",
                table: "GlobalLeaderboardCoops");

        }
    }
}
