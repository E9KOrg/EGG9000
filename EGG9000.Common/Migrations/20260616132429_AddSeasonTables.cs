using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeasonInfos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GoalsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSeasonProgresses",
                columns: table => new
                {
                    EggIncId = table.Column<string>(type: "text", nullable: false),
                    SeasonId = table.Column<string>(type: "text", nullable: false),
                    TotalCxp = table.Column<double>(type: "double precision", nullable: false),
                    StartingGrade = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSeasonProgresses", x => new { x.EggIncId, x.SeasonId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeasonInfos");

            migrationBuilder.DropTable(
                name: "UserSeasonProgresses");
        }
    }
}
