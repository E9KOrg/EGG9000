using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddTachyonSuggestionsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TachyonSuggestionsEnabled",
                table: "Guilds",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TachyonSuggestionsEnabled",
                table: "Guilds");
        }
    }
}
