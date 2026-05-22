using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class AddIndexUserCoopXrefCoopId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_UserCoopXrefs_CoopId'
                      AND object_id = OBJECT_ID('[UserCoopXrefs]')
                )
                BEGIN
                    CREATE INDEX [IX_UserCoopXrefs_CoopId] ON [UserCoopXrefs]([CoopId]);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_UserCoopXrefs_CoopId'
                      AND object_id = OBJECT_ID('[UserCoopXrefs]')
                )
                BEGIN
                    DROP INDEX [IX_UserCoopXrefs_CoopId] ON [UserCoopXrefs];
                END
                """);
        }
    }
}
