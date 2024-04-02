using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace EGG9000.Common.Migrations {
    public partial class RenameDiscordUserAndAddUserStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "DiscordUsers", newName: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_UserCoopXrefs_DiscordUsers_DiscordUserId",
                table: "UserCoopXrefs");

            migrationBuilder.RenameColumn(
                name: "DiscordUserId",
                table: "UserCoopXrefs",
                newName: "UserId");

            migrationBuilder.CreateTable(
                name: "UserCoopStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: true),
                    CoopId = table.Column<Guid>(nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(nullable: false),
                    EggIncId = table.Column<string>(nullable: true),
                    EggIncName = table.Column<string>(nullable: true),
                    Total = table.Column<double>(nullable: false),
                    Rate = table.Column<double>(nullable: false),
                    SleepingWarning = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCoopStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCoopStatuses_Coops_CoopId",
                        column: x => x.CoopId,
                        principalTable: "Coops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCoopStatuses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopStatuses_CoopId",
                table: "UserCoopStatuses",
                column: "CoopId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopStatuses_UserId",
                table: "UserCoopStatuses",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserCoopXrefs_Users_UserId",
                table: "UserCoopXrefs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserCoopXrefs_Users_UserId",
                table: "UserCoopXrefs");

            migrationBuilder.DropTable(
                name: "UserCoopStatuses");

            migrationBuilder.RenameTable(name: "Users", newName: "DiscordUsers");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "UserCoopXrefs",
                newName: "DiscordUserId");


            migrationBuilder.AddForeignKey(
                name: "FK_UserCoopXrefs_DiscordUsers_DiscordUserId",
                table: "UserCoopXrefs",
                column: "DiscordUserId",
                principalTable: "DiscordUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
