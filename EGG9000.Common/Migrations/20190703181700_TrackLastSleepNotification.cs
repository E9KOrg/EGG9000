using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class TrackLastSleepNotification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EggIncNames",
                table: "DiscordUsers");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSleepingNotification",
                table: "DiscordUsers",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSleepingNotification",
                table: "DiscordUsers");

            migrationBuilder.AddColumn<string>(
                name: "EggIncNames",
                table: "DiscordUsers",
                nullable: true);
        }
    }
}
