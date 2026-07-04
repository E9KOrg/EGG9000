using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EGG9000.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtueStatsToUserSnapShot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CuriosityDelivered",
                table: "UserSnapShots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentEgg",
                table: "UserSnapShots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "HumilityDelivered",
                table: "UserSnapShots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "IntegrityDelivered",
                table: "UserSnapShots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "KindnessDelivered",
                table: "UserSnapShots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "Resets",
                table: "UserSnapShots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "ResilienceDelivered",
                table: "UserSnapShots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "ShiftCount",
                table: "UserSnapShots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TeEarned",
                table: "UserSnapShots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "TePending",
                table: "UserSnapShots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeTotal",
                table: "UserSnapShots",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuriosityDelivered",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "CurrentEgg",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "HumilityDelivered",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "IntegrityDelivered",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "KindnessDelivered",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "Resets",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "ResilienceDelivered",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "ShiftCount",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "TeEarned",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "TePending",
                table: "UserSnapShots");

            migrationBuilder.DropColumn(
                name: "TeTotal",
                table: "UserSnapShots");
        }
    }
}
