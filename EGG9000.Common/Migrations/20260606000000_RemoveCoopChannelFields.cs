using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCoopChannelFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
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

            migrationBuilder.DropColumn(
                name: "WarningForDeleteChannel",
                table: "Coops");

            migrationBuilder.CreateIndex(
                name: "IX_Coops_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: ["ThreadArchived", "CoopEnds", "ThreadID"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Coops_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops");

            migrationBuilder.AddColumn<bool>(
                name: "DeletedChannel",
                table: "Coops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscordChannelId",
                table: "Coops",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "FindChannelErrors",
                table: "Coops",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarningForDeleteChannel",
                table: "Coops",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coops_DiscordChannelId_ThreadArchived_CoopEnds_ThreadID",
                table: "Coops",
                columns: ["DiscordChannelId", "ThreadArchived", "CoopEnds", "ThreadID"]);
        }
    }
}
