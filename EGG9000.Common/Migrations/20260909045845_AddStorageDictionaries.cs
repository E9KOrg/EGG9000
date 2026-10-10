using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageDictionaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageDictionaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Corpus = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    TrainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    HoldoutBytesPlain = table.Column<long>(type: "bigint", nullable: false),
                    HoldoutBytesActive = table.Column<long>(type: "bigint", nullable: false),
                    HoldoutBytesCandidate = table.Column<long>(type: "bigint", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageDictionaries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageDictionaries_Corpus_Active",
                table: "StorageDictionaries",
                columns: new[] { "Corpus", "Active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageDictionaries");
        }
    }
}
