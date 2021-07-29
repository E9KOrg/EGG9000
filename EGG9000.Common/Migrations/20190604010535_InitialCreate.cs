using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    ID = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    Rewards = table.Column<string>(nullable: true),
                    Base64Data = table.Column<string>(nullable: true),
                    Created = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "Coops",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ContractID = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    CoopEnds = table.Column<DateTimeOffset>(nullable: false),
                    Created = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coops_Contracts_ContractID",
                        column: x => x.ContractID,
                        principalTable: "Contracts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CoopStatus",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    CoopId = table.Column<Guid>(nullable: true),
                    Created = table.Column<DateTimeOffset>(nullable: false),
                    Base64Data = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoopStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoopStatus_Coops_CoopId",
                        column: x => x.CoopId,
                        principalTable: "Coops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ContractID",
                table: "Coops",
                column: "ContractID");

            migrationBuilder.CreateIndex(
                name: "IX_CoopStatus_CoopId",
                table: "CoopStatus",
                column: "CoopId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoopStatus");

            migrationBuilder.DropTable(
                name: "Coops");

            migrationBuilder.DropTable(
                name: "Contracts");
        }
    }
}
