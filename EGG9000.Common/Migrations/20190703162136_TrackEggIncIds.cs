using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class TrackEggIncIds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "_eggIncIds",
                table: "DiscordUsers",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_eggIncIds",
                table: "DiscordUsers");
        }
    }
}
