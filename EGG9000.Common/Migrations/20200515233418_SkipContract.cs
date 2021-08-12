using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class SkipContract : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Skip",
                table: "GuildContracts",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Skip",
                table: "GuildContracts");
        }
    }
}
