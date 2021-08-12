using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class UserSnapShots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSnapShots",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "Date", nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    EggIncID = table.Column<string>(nullable: false),
                    EggsOfProphecy = table.Column<decimal>(nullable: false),
                    SoulEggs = table.Column<double>(nullable: false),
                    EarningsBonus = table.Column<double>(nullable: false),
                    Prestiges = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSnapShots", x => new { x.UserId, x.Date, x.EggIncID });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSnapShots");
        }
    }
}
