using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EGG9000.Common.Migrations {
    public partial class UpcomingContracts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UpcomingContracts",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuildID = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    TargetDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsLeggacy = table.Column<bool>(type: "bit", nullable: false),
                    _userRegs = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContractId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ChannelId = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpcomingContracts", x => x.ID);
                    table.ForeignKey(
                        name: "FK_UpcomingContracts_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingContracts_ContractId",
                table: "UpcomingContracts",
                column: "ContractId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpcomingContracts");
        }
    }
}
