using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserCsHistoryEntries_ContractIdentifier",
                table: "UserCsHistoryEntries",
                column: "ContractIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_JoinedCoop",
                table: "UserCoopXrefs",
                column: "JoinedCoop");

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_JoinedCoop_CreatedOn",
                table: "UserCoopXrefs",
                columns: new[] { "JoinedCoop", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCoopXrefs_UserId_JoinedCoop",
                table: "UserCoopXrefs",
                columns: new[] { "UserId", "JoinedCoop" });

            migrationBuilder.CreateIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: new[] { "DiscordChannelId", "ThreadArchived", "CoopEnds", "ThreadID" });

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadID",
                table: "Coops",
                column: "ThreadID");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadID_Created",
                table: "Coops",
                columns: new[] { "ThreadID", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserCsHistoryEntries_ContractIdentifier",
                table: "UserCsHistoryEntries");

            migrationBuilder.DropIndex(
                name: "IX_UserCoopXrefs_JoinedCoop",
                table: "UserCoopXrefs");

            migrationBuilder.DropIndex(
                name: "IX_UserCoopXrefs_JoinedCoop_CreatedOn",
                table: "UserCoopXrefs");

            migrationBuilder.DropIndex(
                name: "IX_UserCoopXrefs_UserId_JoinedCoop",
                table: "UserCoopXrefs");

            migrationBuilder.DropIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops");

            migrationBuilder.DropIndex(
                name: "IX_Coops_ThreadID",
                table: "Coops");

            migrationBuilder.DropIndex(
                name: "IX_Coops_ThreadID_Created",
                table: "Coops");
        }
    }
}
