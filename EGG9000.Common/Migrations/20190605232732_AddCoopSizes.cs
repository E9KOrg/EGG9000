using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class AddCoopSizes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentUsers",
                table: "Coops",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsers",
                table: "Coops",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentUsers",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "MaxUsers",
                table: "Coops");
        }
    }
}
