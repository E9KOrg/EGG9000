using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EGG9000.Common.Migrations {
    /// <inheritdoc />
    public partial class AddApiKeyRequestLogging : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "ApiKeyDailyUsages",
                columns: table => new {
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_ApiKeyDailyUsages", x => new { x.ApiKeyId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "ApiKeyRequestLogs",
                columns: table => new {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_ApiKeyRequestLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyRequestLogs_ApiKeyId_Timestamp",
                table: "ApiKeyRequestLogs",
                columns: ["ApiKeyId", "Timestamp"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "ApiKeyDailyUsages");

            migrationBuilder.DropTable(
                name: "ApiKeyRequestLogs");
        }
    }
}
