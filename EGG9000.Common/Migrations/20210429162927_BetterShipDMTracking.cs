using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class BetterShipDMTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "_shipDMsByte",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "_shipDMsString",
                table: "Users",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_shipDMsByte",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "_shipDMsString",
                table: "Users");
        }
    }
}
