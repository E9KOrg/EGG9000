using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class TrackShipDM : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DMOnShipReturn",
                table: "Users",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextShipReturnDMDue",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShipReturnMinutes",
                table: "Users",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ShipReturnStillFuelingMinutes",
                table: "Users",
                nullable: false,
                defaultValue: 0);
                    }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DMOnShipReturn",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NextShipReturnDMDue",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShipReturnMinutes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShipReturnStillFuelingMinutes",
                table: "Users");
        }
    }
}
