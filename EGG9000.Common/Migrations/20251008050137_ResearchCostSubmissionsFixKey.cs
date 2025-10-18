using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class ResearchCostSubmissionsFixKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ResearchCostSubmissions",
                table: "ResearchCostSubmissions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ResearchCostSubmissions",
                table: "ResearchCostSubmissions",
                columns: new[] { "ID", "Level", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ResearchCostSubmissions",
                table: "ResearchCostSubmissions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ResearchCostSubmissions",
                table: "ResearchCostSubmissions",
                column: "ID");
        }
    }
}
