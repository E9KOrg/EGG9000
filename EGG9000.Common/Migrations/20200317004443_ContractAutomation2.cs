using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class ContractAutomation2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AddedToChannel",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WaitingOnStarter",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedToChannel",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "WaitingOnStarter",
                table: "UserCoopXrefs");
        }
    }
}
