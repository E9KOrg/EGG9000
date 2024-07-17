using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EGG9000.Common.Migrations {
    public partial class UserCsHistoryTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCsHistoryEntries",
                columns: table => new
                {
                    ContractIdentifier = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CoopIdentifier = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EggIncId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Cxp = table.Column<double>(type: "float", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCsHistoryEntries", x => new { x.CoopIdentifier, x.ContractIdentifier, x.EggIncId });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCsHistoryEntries");
        }
    }
}
