using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class DropXrefIndexesAndGlobalLeaderboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalLeaderboardCoops");

            migrationBuilder.DropTable(
                name: "GlobalLeaderboardUsers");

            migrationBuilder.DropIndex(
                name: "IX_UserCoopXrefs_CreatedOn_JoinedCoop",
                table: "UserCoopXrefs");

            migrationBuilder.DropIndex(
                name: "IX_UserCoopXrefs_JoinedCoop",
                table: "UserCoopXrefs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardCoops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckFailed = table.Column<bool>(type: "boolean", nullable: false),
                    Checked = table.Column<bool>(type: "boolean", nullable: false),
                    ContractID = table.Column<string>(type: "text", nullable: true),
                    DegreeOfSeperation = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardCoops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardUsers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    DegreeOfSeperation = table.Column<int>(type: "integer", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: true),
                    LastBackup = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NeedsUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateFailed = table.Column<bool>(type: "boolean", nullable: false),
                    earnings_bonus = table.Column<double>(type: "double precision", nullable: false),
                    eggs_of_prophecy = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    lifetime_cash_earned = table.Column<double>(type: "double precision", nullable: false),
                    soul_eggs = table.Column<double>(type: "double precision", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardUsers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_CreatedOn_JoinedCoop",
                table: "UserCoopXrefs",
                columns: new[] { "CreatedOn", "JoinedCoop" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_JoinedCoop",
                table: "UserCoopXrefs",
                column: "JoinedCoop");
        }
    }
}
