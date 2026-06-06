using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class MoveShowContractStatsEmbedsToGuildSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowContractStatsEmbeds",
                table: "Guilds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // ShowContractStatsEmbeds was previously enum value 8 of GuildCoopSetting,
            // stored as an enabled entry inside the _coopSettingsJson array. Move any
            // enabled value onto the new dedicated column.
            migrationBuilder.Sql("""
                UPDATE [Guilds]
                SET [ShowContractStatsEmbeds] = 1
                WHERE [_coopSettingsJson] IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM OPENJSON([_coopSettingsJson])
                      WITH (CoopSetting int '$.CoopSetting', Enabled bit '$.Enabled')
                      WHERE CoopSetting = 8 AND Enabled = 1
                  );
                """);

            // Drop the now-orphaned enum-8 entries from the coop settings array so the
            // stored JSON no longer carries a value the enum no longer defines.
            migrationBuilder.Sql("""
                UPDATE [Guilds]
                SET [_coopSettingsJson] = (
                    SELECT '[' + ISNULL(STRING_AGG(
                        '{"CoopSetting":' + CAST(CoopSetting AS varchar(10)) +
                        ',"Enabled":' + CASE WHEN Enabled = 1 THEN 'true' ELSE 'false' END +
                        ',"Locked":' + CASE WHEN Locked = 1 THEN 'true' ELSE 'false' END + '}',
                        ','), '') + ']'
                    FROM OPENJSON([_coopSettingsJson])
                    WITH (CoopSetting int '$.CoopSetting', Enabled bit '$.Enabled', Locked bit '$.Locked')
                    WHERE CoopSetting <> 8
                )
                WHERE [_coopSettingsJson] IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM OPENJSON([_coopSettingsJson])
                      WITH (CoopSetting int '$.CoopSetting')
                      WHERE CoopSetting = 8
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-materialize the value as the old enum-8 coop setting entry before dropping the column.
            migrationBuilder.Sql("""
                UPDATE [Guilds]
                SET [_coopSettingsJson] = JSON_MODIFY(
                        ISNULL(NULLIF([_coopSettingsJson], ''), '[]'),
                        'append $',
                        JSON_QUERY('{"CoopSetting":8,"Enabled":true,"Locked":false}'))
                WHERE [ShowContractStatsEmbeds] = 1;
                """);

            migrationBuilder.DropColumn(
                name: "ShowContractStatsEmbeds",
                table: "Guilds");
        }
    }
}
