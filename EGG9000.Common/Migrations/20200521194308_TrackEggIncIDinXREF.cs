using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class TrackEggIncIDinXREF : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserCoopXrefs",
                table: "UserCoopXrefs");

            migrationBuilder.AddColumn<string>(
                name: "EggIncId",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserCoopXrefs",
                table: "UserCoopXrefs",
                columns: new[] { "UserId", "CoopId", "EggIncId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserCoopXrefs",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "EggIncId",
                table: "UserCoopXrefs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserCoopXrefs",
                table: "UserCoopXrefs",
                columns: new[] { "UserId", "CoopId" });
        }
    }
}
