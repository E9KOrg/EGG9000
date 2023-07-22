using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class SubscriptionContracts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CcOnly",
                table: "GuildContracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CcOnly",
                table: "Events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnyLeague",
                table: "Coops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "cc_only",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CcOnly",
                table: "GuildContracts");

            migrationBuilder.DropColumn(
                name: "CcOnly",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AnyLeague",
                table: "Coops");

            migrationBuilder.DropColumn(
                name: "cc_only",
                table: "Contracts");
        }
    }
}
