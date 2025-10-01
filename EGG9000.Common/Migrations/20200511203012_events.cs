using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class Eventsm : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    Identifier = table.Column<string>(nullable: true),
                    Ends = table.Column<DateTimeOffset>(nullable: false),
                    Type = table.Column<string>(nullable: true),
                    Multiplier = table.Column<double>(nullable: false),
                    Subtitle = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
