using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class Demerits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Demerit",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    When = table.Column<DateTimeOffset>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    AdminUserId = table.Column<Guid>(nullable: false),
                    Reason = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demerit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Demerit_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Demerit_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Demerit_AdminUserId",
                table: "Demerit",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Demerit_UserId",
                table: "Demerit",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Demerit");
        }
    }
}
