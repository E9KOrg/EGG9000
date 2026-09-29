using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddRemovedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemovedAccounts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EggIncId = table.Column<string>(type: "text", nullable: false),
                    RemovedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedByDiscordId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ReappliedCount = table.Column<int>(type: "integer", nullable: false),
                    LastReappliedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemovedAccounts", x => new { x.UserId, x.EggIncId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemovedAccounts_EggIncId",
                table: "RemovedAccounts",
                column: "EggIncId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemovedAccounts");
        }
    }
}
