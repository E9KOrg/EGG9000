using Microsoft.EntityFrameworkCore.Migrations;

namespace EGG9000.Common.Migrations {
    public partial class UpdateMessage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpdateMessagesId",
                table: "Coops",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdateMessagesId",
                table: "Coops");
        }
    }
}
