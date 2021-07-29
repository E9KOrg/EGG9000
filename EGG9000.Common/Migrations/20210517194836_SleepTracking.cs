using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class SleepTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "_sleepTrackingByte",
                table: "UserCoopXrefs",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DemeritLogChannel",
                table: "Guilds",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_sleepTrackingByte",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "DemeritLogChannel",
                table: "Guilds");

        }
    }
}
