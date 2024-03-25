using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class ContractAutomation1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JoinedCoop",
                table: "UserCoopXrefs",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "_response",
                table: "Contracts",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GuildContracts",
                columns: table => new
                {
                    ContractID = table.Column<string>(nullable: false),
                    GuildID = table.Column<decimal>(nullable: false),
                    DiscordChannelId = table.Column<decimal>(nullable: false),
                    WarningForDeleteChannel = table.Column<DateTimeOffset>(nullable: true),
                    DeletedChannel = table.Column<bool>(nullable: false),
                    NumberOfCoops = table.Column<int>(nullable: false),
                    Starters = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: false),
                    Created = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildContracts", x => new { x.ContractID, x.GuildID });
                    table.ForeignKey(
                        name: "FK_GuildContracts_Contracts_ContractID",
                        column: x => x.ContractID,
                        principalTable: "Contracts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildContracts");

            migrationBuilder.DropColumn(
                name: "JoinedCoop",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "_response",
                table: "Contracts");
        }
    }
}
