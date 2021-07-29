using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class JoinWarnings2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning12h",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24TillFinish",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24h",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinWarning12h",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "JoinWarning24TillFinish",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "JoinWarning24h",
                table: "UserCoopXrefs");

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning12h",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24TillFinish",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JoinWarning24h",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

        }
    }
}
