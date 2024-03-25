using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations {
    public partial class HasScoresForGuildContract : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "League",
                table: "GuildContracts");

            migrationBuilder.AddColumn<bool>(
                name: "HasScores",
                table: "GuildContracts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasScores",
                table: "GuildContracts");

            migrationBuilder.AddColumn<int>(
                name: "League",
                table: "GuildContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
