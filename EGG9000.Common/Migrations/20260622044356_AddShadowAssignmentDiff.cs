using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddShadowAssignmentDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShadowAssignmentDiffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContractId = table.Column<string>(type: "text", nullable: true),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: true),
                    DiscordId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    LiveAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    ShadowAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    LiveReason = table.Column<string>(type: "text", nullable: true),
                    ShadowReason = table.Column<string>(type: "text", nullable: true),
                    ExpectedSeasonalDeviation = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShadowAssignmentDiffs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShadowAssignmentDiffs");
        }
    }
}
