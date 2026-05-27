using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class AddIndexGuildContractDiscordChannelId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_GuildContracts_DiscordChannelId'
                      AND object_id = OBJECT_ID('[GuildContracts]')
                )
                BEGIN
                    CREATE INDEX [IX_GuildContracts_DiscordChannelId] ON [GuildContracts]([DiscordChannelId]);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_GuildContracts_DiscordChannelId'
                      AND object_id = OBJECT_ID('[GuildContracts]')
                )
                BEGIN
                    DROP INDEX [IX_GuildContracts_DiscordChannelId] ON [GuildContracts];
                END
                """);
        }
    }
}
