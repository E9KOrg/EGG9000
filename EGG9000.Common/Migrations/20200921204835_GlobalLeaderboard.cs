using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class GlobalLeaderboard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardCoops",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    ContractID = table.Column<string>(nullable: true),
                    Checked = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardCoops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalLeaderboardUsers",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    NeedsUpdate = table.Column<bool>(nullable: false),
                    LastUpdate = table.Column<DateTimeOffset>(nullable: true),
                    EggIncId = table.Column<long>(nullable: false),
                    eggs_of_prophecy = table.Column<decimal>(nullable: false),
                    soul_eggs = table.Column<double>(nullable: false),
                    earings_bnous = table.Column<double>(nullable: false),
                    lifetime_cash_earned = table.Column<double>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLeaderboardUsers", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalLeaderboardCoops");

            migrationBuilder.DropTable(
                name: "GlobalLeaderboardUsers");

        }
    }
}
