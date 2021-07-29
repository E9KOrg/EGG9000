using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class Updates2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CoopStatus_Coops_CoopId",
                table: "CoopStatus");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CoopStatus",
                table: "CoopStatus");

            migrationBuilder.RenameTable(
                name: "CoopStatus",
                newName: "CoopStatuses");

            migrationBuilder.RenameIndex(
                name: "IX_CoopStatus_CoopId",
                table: "CoopStatuses",
                newName: "IX_CoopStatuses_CoopId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoopStatuses",
                table: "CoopStatuses",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "DiscordUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    DiscordId = table.Column<decimal>(nullable: false),
                    EggIncIds = table.Column<string>(nullable: true),
                    CreateOn = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserCoopXrefs",
                columns: table => new
                {
                    DiscordUserId = table.Column<Guid>(nullable: false),
                    CoopId = table.Column<Guid>(nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCoopXrefs", x => new { x.DiscordUserId, x.CoopId });
                    table.ForeignKey(
                        name: "FK_UserCoopXrefs_Coops_CoopId",
                        column: x => x.CoopId,
                        principalTable: "Coops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCoopXrefs_DiscordUsers_DiscordUserId",
                        column: x => x.DiscordUserId,
                        principalTable: "DiscordUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_CoopId",
                table: "UserCoopXrefs",
                column: "CoopId");

            migrationBuilder.AddForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses",
                column: "CoopId",
                principalTable: "Coops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses");

            migrationBuilder.DropTable(
                name: "UserCoopXrefs");

            migrationBuilder.DropTable(
                name: "DiscordUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CoopStatuses",
                table: "CoopStatuses");

            migrationBuilder.RenameTable(
                name: "CoopStatuses",
                newName: "CoopStatus");

            migrationBuilder.RenameIndex(
                name: "IX_CoopStatuses_CoopId",
                table: "CoopStatus",
                newName: "IX_CoopStatus_CoopId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoopStatus",
                table: "CoopStatus",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CoopStatus_Coops_CoopId",
                table: "CoopStatus",
                column: "CoopId",
                principalTable: "Coops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
