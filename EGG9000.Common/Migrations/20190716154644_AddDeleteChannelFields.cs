using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class AddDeleteChannelFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeletedChannel",
                table: "Coops",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarningForDeleteChannel",
                table: "Coops",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedChannel",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "WarningForDeleteChannel",
                table: "Coops");
        }
    }
}
