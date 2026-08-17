using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class DropV1CoopColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops");

            migrationBuilder.DropIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "DeletedChannel",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "DiscordChannelId",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "FindChannelErrors",
                table: "Coops");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops",
                columns: new[] { "GuildId", "ContractID", "League" },
                filter: "NOT \"Finished\" AND NOT \"ThreadArchived\"");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: new[] { "ThreadArchived", "CoopEnds", "ThreadID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops");

            migrationBuilder.DropIndex(
                name: "IX_Coops_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops");

            migrationBuilder.AddColumn<bool>(
                name: "DeletedChannel",
                table: "Coops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscordChannelId",
                table: "Coops",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "FindChannelErrors",
                table: "Coops",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: new[] { "DiscordChannelId", "ThreadArchived", "CoopEnds", "ThreadID" });

            migrationBuilder.CreateIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops",
                columns: new[] { "GuildId", "ContractID", "League" },
                filter: "NOT \"Finished\" AND NOT \"DeletedChannel\" AND NOT \"ThreadArchived\"");
        }
    }
}
