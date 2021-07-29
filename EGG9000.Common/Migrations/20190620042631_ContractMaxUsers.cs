using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class ContractMaxUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxUsers",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxUsers",
                table: "Contracts");
        }
    }
}
