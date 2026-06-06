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
                type: "int",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<bool>(
                name: "RankupExclusivePool",
                table: "Guilds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RankupMessagesEnabled",
                table: "Guilds",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "_rankupDisabledGroupsCsv",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankupMessages",
                columns: table => new
                {
                    InternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    GuildName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GroupBaseOom = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    PalaceOnly = table.Column<bool>(type: "bit", nullable: false),
                    _subscribedGuildIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByIdString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
