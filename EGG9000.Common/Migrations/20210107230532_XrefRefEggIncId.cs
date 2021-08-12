using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class XrefRefEggIncId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefEggIncId",
                table: "UserCoopXrefs",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefEggIncId",
                table: "UserCoopXrefs");
        }
    }
}
