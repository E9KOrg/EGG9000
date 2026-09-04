using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSeasonResponseBlobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "_response",
                table: "SeasonInfos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "_response",
                table: "Events",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_response",
                table: "SeasonInfos");

            migrationBuilder.DropColumn(
                name: "_response",
                table: "Events");
        }
    }
}
