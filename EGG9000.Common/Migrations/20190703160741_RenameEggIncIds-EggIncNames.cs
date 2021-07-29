using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
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
