using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class TrackScores : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_LastBackup",
                table: "Users");

            migrationBuilder.AddColumn<float>(
                name: "RunningScore",
                table: "UserCoopXrefs",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "Score",
                table: "UserCoopXrefs",
                type: "real",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunningScore",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "UserCoopXrefs");

            migrationBuilder.AddColumn<byte[]>(
                name: "_LastBackup",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);
        }
    }
}
