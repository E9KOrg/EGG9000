using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class GlobalLeaderboard3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "earings_bnous",
                table: "GlobalLeaderboardUsers");

            migrationBuilder.AlterColumn<string>(
                name: "EggIncId",
                table: "GlobalLeaderboardUsers",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<double>(
                name: "earnings_bonus",
                table: "GlobalLeaderboardUsers",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "earnings_bonus",
                table: "GlobalLeaderboardUsers");

            migrationBuilder.AlterColumn<long>(
                name: "EggIncId",
                table: "GlobalLeaderboardUsers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AddColumn<double>(
                name: "earings_bnous",
                table: "GlobalLeaderboardUsers",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
