using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class FAQTopicsExtras : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFAQPosted",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FAQTopicCooldownMinutes",
                table: "Guilds",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "FAQTopicsEnabled",
                table: "Guilds",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastFAQPosted",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FAQTopicCooldownMinutes",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "FAQTopicsEnabled",
                table: "Guilds");
        }
    }
}
