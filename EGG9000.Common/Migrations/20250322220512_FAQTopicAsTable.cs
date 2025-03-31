using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class FAQTopicAsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FAQTopics",
                columns: table => new
                {
                    InternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    _keywords = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StaffOnly = table.Column<bool>(type: "bit", nullable: false),
                    PalaceOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByIdString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuildName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuildIdString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    _subscribedGuildIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmbedColorHex = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAQTopics", x => x.InternalId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FAQTopics");
        }
    }
}
