using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class XrefDeflectorStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EquipedTachyonDeflector",
                table: "UserCoopXrefs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasTachyonDeflector",
                table: "UserCoopXrefs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquipedTachyonDeflector",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "HasTachyonDeflector",
                table: "UserCoopXrefs");
        }
    }
}
