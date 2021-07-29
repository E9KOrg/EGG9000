using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class AddContractGoodUntil : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GoodUntil",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoodUntil",
                table: "Contracts");
        }
    }
}
