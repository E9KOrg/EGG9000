using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class CustomEggsReplacement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_eggs",
                table: "Contracts");

            migrationBuilder.AddColumn<double>(
                name: "egg_value",
                table: "Contracts",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "egg_value",
                table: "Contracts");

            migrationBuilder.AddColumn<string>(
                name: "custom_eggs",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
