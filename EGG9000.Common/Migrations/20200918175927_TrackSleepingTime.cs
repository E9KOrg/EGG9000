using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class TrackSleepingTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HoursSleeping",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SleepingDiscordMessageID",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<float>(
                name: "TotalHoursSleeping",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: 0f);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoursSleeping",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "SleepingDiscordMessageID",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "TotalHoursSleeping",
                table: "UserCoopXrefs");
        }
    }
}
