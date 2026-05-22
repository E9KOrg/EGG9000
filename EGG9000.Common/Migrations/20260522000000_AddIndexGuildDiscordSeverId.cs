using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class AddIndexGuildDiscordSeverId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Guilds_DiscordSeverId'
                      AND object_id = OBJECT_ID('[Guilds]')
                )
                BEGIN
                    CREATE INDEX [IX_Guilds_DiscordSeverId] ON [Guilds]([DiscordSeverId]);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Guilds_DiscordSeverId'
                      AND object_id = OBJECT_ID('[Guilds]')
                )
                BEGIN
                    DROP INDEX [IX_Guilds_DiscordSeverId] ON [Guilds];
                END
                """);
        }
    }
}
