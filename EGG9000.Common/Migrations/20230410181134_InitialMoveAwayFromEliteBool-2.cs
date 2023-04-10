using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class InitialMoveAwayFromEliteBool2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts");

            //migrationBuilder.AlterColumn<long>(
            //    name: "League",
            //    table: "Coops",
            //    type: "bigint",
            //    nullable: false,
            //    defaultValue: 0L,
            //    oldClrType: typeof(long),
            //    oldType: "bigint",
            //    oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts",
                columns: new[] { "ContractID", "GuildID", "League" });
        }

        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts");

            //migrationBuilder.AlterColumn<long>(
            //    name: "League",
            //    table: "Coops",
            //    type: "bigint",
            //    nullable: true,
            //    oldClrType: typeof(long),
            //    oldType: "bigint");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildContracts",
                table: "GuildContracts",
                columns: new[] { "ContractID", "GuildID", "Elite" });
        }
    }
}
