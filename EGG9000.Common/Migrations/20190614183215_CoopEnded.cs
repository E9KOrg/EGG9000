using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class CoopEnded : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Finished",
                table: "Coops",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProjectedToFinish",
                table: "Coops",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Finished",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "ProjectedToFinish",
                table: "Coops");
        }
    }
}
