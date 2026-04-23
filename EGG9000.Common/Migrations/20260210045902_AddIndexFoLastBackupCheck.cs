using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexFoLastBackupCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Users', 'LastBackupCheck') IS NULL
                BEGIN
                    ALTER TABLE [Users] ADD [LastBackupCheck] datetimeoffset NULL;
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Users_LastBackupCheck'
                      AND object_id = OBJECT_ID('[Users]')
                )
                BEGIN
                    CREATE INDEX [IX_Users_LastBackupCheck] ON [Users]([LastBackupCheck]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Users_LastBackupCheck'
                      AND object_id = OBJECT_ID('[Users]')
                )
                BEGIN
                    DROP INDEX [IX_Users_LastBackupCheck] ON [Users];
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('Users', 'LastBackupCheck') IS NOT NULL
                BEGIN
                    ALTER TABLE [Users] DROP COLUMN [LastBackupCheck];
                END
                """);
        }
    }
}
