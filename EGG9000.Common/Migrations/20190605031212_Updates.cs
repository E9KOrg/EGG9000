using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class Updates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Base64Data",
                table: "Contracts");

            migrationBuilder.AddColumn<int>(
                name: "P11",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "P2",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "P4",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "P6",
                table: "Contracts",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "P7",
                table: "Contracts",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "P11",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P2",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P4",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P6",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P7",
                table: "Contracts");

            migrationBuilder.AddColumn<string>(
                name: "Base64Data",
                table: "Contracts",
                nullable: true);
        }
    }
}
