using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineJoinTimingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Removed",
                table: "UserCoopXrefs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedOn",
                table: "UserCoopXrefs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JoinTimeHours",
                table: "Guilds",
                type: "integer",
                nullable: false,
                defaultValue: 18);

            migrationBuilder.AddColumn<int>(
                name: "JoinTimeUltraHours",
                table: "Guilds",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "OfflineDemeritHours",
                table: "Guilds",
                type: "integer",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Removed",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "RemovedOn",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "JoinTimeHours",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "JoinTimeUltraHours",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "OfflineDemeritHours",
                table: "Guilds");
        }
    }
}
