using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    public partial class MoveShowContractStatsEmbedsToGuildSetting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE name = 'ShowContractStatsEmbeds'
                      AND object_id = OBJECT_ID('[Guilds]')
                )
                BEGIN
                    ALTER TABLE [Guilds] ADD [ShowContractStatsEmbeds] bit NOT NULL
                        CONSTRAINT [DF_Guilds_ShowContractStatsEmbeds] DEFAULT 0;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE name = 'ShowContractStatsEmbeds'
                      AND object_id = OBJECT_ID('[Guilds]')
                )
                BEGIN
                    ALTER TABLE [Guilds] DROP CONSTRAINT [DF_Guilds_ShowContractStatsEmbeds];
                    ALTER TABLE [Guilds] DROP COLUMN [ShowContractStatsEmbeds];
                END
                """);
        }
    }
}
