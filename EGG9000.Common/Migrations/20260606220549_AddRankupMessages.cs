using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260606220549_AddRankupMessages")]
    public partial class AddRankupMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('[Users]', 'HighestAnnouncedOom') IS NULL
                    ALTER TABLE [Users] ADD [HighestAnnouncedOom] int NOT NULL DEFAULT -1;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('[Guilds]', 'RankupMessagesEnabled') IS NULL
                    ALTER TABLE [Guilds] ADD [RankupMessagesEnabled] bit NOT NULL DEFAULT 1;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('[Guilds]', 'RankupExclusivePool') IS NULL
                    ALTER TABLE [Guilds] ADD [RankupExclusivePool] bit NOT NULL DEFAULT 0;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('[Guilds]', '_rankupDisabledGroupsCsv') IS NULL
                    ALTER TABLE [Guilds] ADD [_rankupDisabledGroupsCsv] nvarchar(max) NULL;
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID('[RankupMessages]', 'U') IS NULL
                BEGIN
                    CREATE TABLE [RankupMessages] (
                        [InternalId] nvarchar(450) NOT NULL,
                        [GuildId] decimal(20,0) NOT NULL,
                        [GuildName] nvarchar(max) NULL,
                        [GroupBaseOom] int NOT NULL,
                        [Text] nvarchar(max) NULL,
                        [Weight] int NOT NULL,
                        [PalaceOnly] bit NOT NULL,
                        [_subscribedGuildIds] nvarchar(max) NULL,
                        [CreatedByIdString] nvarchar(max) NULL,
                        [CreatedById] decimal(20,0) NOT NULL,
                        [CreatedBy] nvarchar(max) NULL,
                        CONSTRAINT [PK_RankupMessages] PRIMARY KEY ([InternalId])
                    );
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('[RankupMessages]', 'U') IS NOT NULL DROP TABLE [RankupMessages];");
            DropColumnWithDefault(migrationBuilder, "Users", "HighestAnnouncedOom");
            DropColumnWithDefault(migrationBuilder, "Guilds", "RankupMessagesEnabled");
            DropColumnWithDefault(migrationBuilder, "Guilds", "RankupExclusivePool");
            DropColumnWithDefault(migrationBuilder, "Guilds", "_rankupDisabledGroupsCsv");
        }

        private static void DropColumnWithDefault(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($"""
                DECLARE @cn sysname;
                SELECT @cn = dc.name
                FROM sys.default_constraints dc
                JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID('[{table}]') AND c.name = '{column}';
                IF @cn IS NOT NULL EXEC('ALTER TABLE [{table}] DROP CONSTRAINT [' + @cn + ']');
                IF COL_LENGTH('[{table}]', '{column}') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [{column}];
                """);
        }
    }
}
