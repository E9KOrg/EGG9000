using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class RemoveNeedForTrackingLastStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastStatusTime",
                table: "UserCoopXrefs",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SleepingWarningTime",
                table: "UserCoopXrefs",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "UserCoopXrefs",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastStatusTime",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "SleepingWarningTime",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "UserCoopXrefs");
        }
    }
}
