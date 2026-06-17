using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddRankupMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HighestAnnouncedOom",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RankupExclusivePool",
                table: "Guilds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RankupMessagesEnabled",
                table: "Guilds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "_rankupDisabledGroupsCsv",
                table: "Guilds",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankupMessages",
                columns: table => new
                {
                    InternalId = table.Column<string>(type: "text", nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GuildName = table.Column<string>(type: "text", nullable: true),
                    GroupBaseOom = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    PalaceOnly = table.Column<bool>(type: "boolean", nullable: false),
                    _subscribedGuildIds = table.Column<string>(type: "text", nullable: true),
                    CreatedByIdString = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankupMessages", x => x.InternalId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankupMessages");

            migrationBuilder.DropColumn(
                name: "HighestAnnouncedOom",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RankupExclusivePool",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "RankupMessagesEnabled",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "_rankupDisabledGroupsCsv",
                table: "Guilds");
        }
    }
}
