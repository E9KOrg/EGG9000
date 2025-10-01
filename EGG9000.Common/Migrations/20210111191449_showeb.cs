using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class Showeb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "showEB",
                table: "Users",
                nullable: false,
                defaultValue: false);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "showEB",
                table: "Users");
         
        }
    }
}
