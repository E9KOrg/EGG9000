using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddCoopsActiveIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops",
                columns: new[] { "GuildId", "ContractID", "League" },
                filter: "NOT \"Finished\" AND NOT \"DeletedChannel\" AND NOT \"ThreadArchived\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Coops_GuildId_ContractID_League",
                table: "Coops");
        }
    }
}
