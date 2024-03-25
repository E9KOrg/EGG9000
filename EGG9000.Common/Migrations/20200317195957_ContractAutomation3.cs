using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class ContractAutomation3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Starter",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Coops",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Starter",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Coops");
        }
    }
}
