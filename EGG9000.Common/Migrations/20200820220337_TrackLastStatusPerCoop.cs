using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations
{
    public partial class TrackLastStatusPerCoop : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<byte[]>(
                name: "_StatusCompressed",
                table: "Coops",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "_StatusCompressed",
                table: "Coops");
        }
    }
}
