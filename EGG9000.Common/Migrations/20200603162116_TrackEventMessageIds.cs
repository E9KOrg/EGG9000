using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class TrackEventMessageIds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "MessageIds",
                table: "Events",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageIds",
                table: "Events");

            migrationBuilder.AddColumn<decimal>(
                name: "MessageId",
                table: "Events",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
