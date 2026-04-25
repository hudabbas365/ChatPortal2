using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class ExtendNotificationsForV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Notification new columns ─────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "TargetUserIdsCsv",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRolesCsv",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduleAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "Notifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Delivered");

            migrationBuilder.AddColumn<bool>(
                name: "IsRecalled",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecalledAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecalledByUserId",
                table: "Notifications",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill: existing notifications get DeliveryStatus="Delivered" and DeliveredAt=CreatedAt
            migrationBuilder.Sql(
                "UPDATE Notifications SET DeliveryStatus = 'Delivered', DeliveredAt = CreatedAt WHERE DeliveryStatus = 'Delivered' OR DeliveryStatus IS NULL OR DeliveryStatus = ''");

            // ── UserNotification new columns ─────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsClicked",
                table: "UserNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClickedAt",
                table: "UserNotifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailSent",
                table: "UserNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // ── NotificationTemplates table ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            // ── Unique index on (UserId, NotificationId) to prevent duplicate fan-out rows ──
            // First remove any duplicates keeping the lowest Id
            migrationBuilder.Sql(
                @"WITH CTE AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY UserId, NotificationId ORDER BY Id) AS rn
                    FROM UserNotifications
                )
                DELETE FROM CTE WHERE rn > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_NotificationId",
                table: "UserNotifications",
                columns: new[] { "UserId", "NotificationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserNotifications_UserId_NotificationId",
                table: "UserNotifications");

            migrationBuilder.DropTable(name: "NotificationTemplates");

            migrationBuilder.DropColumn(name: "TargetUserIdsCsv", table: "Notifications");
            migrationBuilder.DropColumn(name: "TargetRolesCsv", table: "Notifications");
            migrationBuilder.DropColumn(name: "ScheduleAt", table: "Notifications");
            migrationBuilder.DropColumn(name: "DeliveredAt", table: "Notifications");
            migrationBuilder.DropColumn(name: "DeliveryStatus", table: "Notifications");
            migrationBuilder.DropColumn(name: "IsRecalled", table: "Notifications");
            migrationBuilder.DropColumn(name: "RecalledAt", table: "Notifications");
            migrationBuilder.DropColumn(name: "RecalledByUserId", table: "Notifications");

            migrationBuilder.DropColumn(name: "IsClicked", table: "UserNotifications");
            migrationBuilder.DropColumn(name: "ClickedAt", table: "UserNotifications");
            migrationBuilder.DropColumn(name: "EmailSent", table: "UserNotifications");
        }
    }
}
