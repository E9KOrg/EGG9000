using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EGG9000.Common.Migrations {
    public partial class AddingDetailsToGuild : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomCoopName",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpireCustomCoopName",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChannelWarningMessageForUser",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoopNamePrefix",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EliteCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FailedCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GameEventsChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GeneralChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LeaderboardChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaderboardImage",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RulesChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffCoopsMessageDetails",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardCategory",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WelcomeChannel",
                table: "Guilds",
                type: "decimal(20,0)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomCoopName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExpireCustomCoopName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChannelWarningMessageForUser",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "CoopNamePrefix",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "EliteCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "FailedCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "GameEventsChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "GeneralChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "LeaderboardChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "LeaderboardImage",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "RulesChannel",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "StaffCoopsMessageDetails",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "StandardCategory",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "WelcomeChannel",
                table: "Guilds");
        }
    }
}
