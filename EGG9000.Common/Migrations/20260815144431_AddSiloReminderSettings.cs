using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddSiloReminderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SiloWarningFirst",
                table: "UserCoopXrefs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SiloWarningSecond",
                table: "UserCoopXrefs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SiloReminderFirstHours",
                table: "Guilds",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "SiloReminderSecondHours",
                table: "Guilds",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<bool>(
                name: "SiloRemindersEnabled",
                table: "Guilds",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiloWarningFirst",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "SiloWarningSecond",
                table: "UserCoopXrefs");

            migrationBuilder.DropColumn(
                name: "SiloReminderFirstHours",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "SiloReminderSecondHours",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "SiloRemindersEnabled",
                table: "Guilds");
        }
    }
}
