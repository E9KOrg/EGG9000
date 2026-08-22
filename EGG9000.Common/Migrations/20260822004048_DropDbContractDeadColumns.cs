using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class DropDbContractDeadColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P11",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P2",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P4",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "P6",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Rewards",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "coop_allowed",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "debug",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "egg_value",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "goals",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "P11",
                table: "Contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "P2",
                table: "Contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "P4",
                table: "Contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "P6",
                table: "Contracts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Rewards",
                table: "Contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "coop_allowed",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "debug",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "egg_value",
                table: "Contracts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "goals",
                table: "Contracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_boosts",
                table: "Contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "max_soul_eggs",
                table: "Contracts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "min_client_version",
                table: "Contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
