using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class RenameEggIncIdsEggIncNames : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EggIncIds",
                table: "DiscordUsers",
                newName: "EggIncNames");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EggIncNames",
                table: "DiscordUsers",
                newName: "EggIncIds");
        }
    }
}
