using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class Apiupdates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "coop_allowed",
                table: "Contracts",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "debug",
                table: "Contracts",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "egg",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "goals",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "length_seconds",
                table: "Contracts",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "max_boosts",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "max_soul_eggs",
                table: "Contracts",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "min_client_version",
                table: "Contracts",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coop_allowed",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "debug",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "egg",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "goals",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "length_seconds",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "max_boosts",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "max_soul_eggs",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "min_client_version",
                table: "Contracts");
        }
    }
}
