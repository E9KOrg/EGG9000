using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class CoopThreads : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ThreadArchived",
                table: "Coops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ThreadID",
                table: "Coops",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ThreadParentChannel",
                table: "Coops",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThreadArchived",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "ThreadID",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "ThreadParentChannel",
                table: "Coops");
        }
    }
}
