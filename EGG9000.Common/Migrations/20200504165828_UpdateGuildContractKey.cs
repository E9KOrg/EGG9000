using Microsoft.EntityFrameworkCore.Migrations;

namespace DiscordCoopCodes.Migrations
{
    public partial class UpdateGuildContractKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts",
                columns: new[] { "ContractID", "GuildID", "Elite" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts",
                columns: new[] { "ContractID", "GuildID" });
        }
    }
}
