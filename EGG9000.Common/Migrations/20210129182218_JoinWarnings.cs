using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class JoinWarnings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning12h",
                table: "Users",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24TillFinish",
                table: "Users",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24h",
                table: "Users",
                nullable: false,
                defaultValue: false);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinWarning12h",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JoinWarning24TillFinish",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JoinWarning24h",
                table: "Users");

        }
    }
}
