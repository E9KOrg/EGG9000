using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations {
    public partial class TrackSoulPowerForXrefs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SoulPower",
                table: "UserCoopXrefs",
                type: "float",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SoulPower",
                table: "UserCoopXrefs");
        }
    }
}
