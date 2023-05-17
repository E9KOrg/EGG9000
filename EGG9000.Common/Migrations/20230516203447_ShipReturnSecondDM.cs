using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class ShipReturnSecondDM : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_shipDMsString",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "ShipReturnDMAfterFuel",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipReturnDMAfterFuel",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "_shipDMsString",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
