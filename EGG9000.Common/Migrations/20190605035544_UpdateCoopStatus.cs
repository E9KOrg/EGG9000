using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class UpdateCoopStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoopId",
                table: "CoopStatuses",
                nullable: false,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses",
                column: "CoopId",
                principalTable: "Coops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoopId",
                table: "CoopStatuses",
                nullable: true,
                oldClrType: typeof(Guid));

            migrationBuilder.AddForeignKey(
                name: "FK_CoopStatuses_Coops_CoopId",
                table: "CoopStatuses",
                column: "CoopId",
                principalTable: "Coops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
