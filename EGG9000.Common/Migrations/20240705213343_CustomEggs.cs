using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class CustomEggs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomEggs",
                columns: table => new
                {
                    Identifier = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<double>(type: "float", nullable: false),
                    _iconBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    _modifiersBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    EmojiName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmojiId = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomEggs", x => x.Identifier);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomEggs");
        }
    }
}
