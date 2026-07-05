using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations {
    /// <inheritdoc />
    public partial class AddApiKeyRequestLogEndpoint : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "ApiKeyRequestLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "ApiKeyRequestLogs");
        }
    }
}
