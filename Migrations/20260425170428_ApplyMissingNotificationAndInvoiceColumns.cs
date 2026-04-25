using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <summary>
    /// Idempotent fix-up migration. Several earlier migrations were recorded in
    /// __EFMigrationsHistory but their schema changes never actually ran against
    /// the database (e.g. ExtendNotificationsForV2, AddLastSeenAt,
    /// AddLicenseTermsToOrganization, AddInvoiceMetadataToPaymentRecord, etc.).
    /// This migration uses guarded SQL so it can safely add only the columns,
    /// indexes and tables that are actually missing.
    /// </summary>
    public partial class ApplyMissingNotificationAndInvoiceColumns : Migration
    {
        private static string AddColumnIfMissing(string table, string column, string definition) =>
            $@"IF COL_LENGTH(N'[dbo].[{table}]', N'{column}') IS NULL
   ALTER TABLE [dbo].[{table}] ADD [{column}] {definition};";

        protected override void Up(MigrationBuilder mb)
        {
            // ── UserNotifications ────────────────────────────────────────────
            mb.Sql(AddColumnIfMissing("UserNotifications", "ClickedAt", "datetime2 NULL"));
            mb.Sql(AddColumnIfMissing("UserNotifications", "EmailSent", "bit NOT NULL CONSTRAINT DF_UserNotifications_EmailSent DEFAULT (0)"));
            mb.Sql(AddColumnIfMissing("UserNotifications", "IsClicked", "bit NOT NULL CONSTRAINT DF_UserNotifications_IsClicked DEFAULT (0)"));

            // ── PaymentRecords ───────────────────────────────────────────────
            mb.Sql(AddColumnIfMissing("PaymentRecords", "DueDate", "datetime2 NULL"));

            // ── Organizations ────────────────────────────────────────────────
            mb.Sql(AddColumnIfMissing("Organizations", "AutoRenew",        "bit NOT NULL CONSTRAINT DF_Organizations_AutoRenew DEFAULT (0)"));
            mb.Sql(AddColumnIfMissing("Organizations", "LicenseEndsAt",    "datetime2 NULL"));
            mb.Sql(AddColumnIfMissing("Organizations", "LicenseNotes",     "nvarchar(max) NULL"));
            mb.Sql(AddColumnIfMissing("Organizations", "LicenseStartsAt",  "datetime2 NULL"));

            // ── Notifications (the columns from the lost ExtendNotificationsForV2) ──
            mb.Sql(AddColumnIfMissing("Notifications", "DeliveredAt",       "datetime2 NULL"));
            mb.Sql(AddColumnIfMissing("Notifications", "DeliveryStatus",    "nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_DeliveryStatus DEFAULT (N'Delivered')"));
            mb.Sql(AddColumnIfMissing("Notifications", "IsRecalled",        "bit NOT NULL CONSTRAINT DF_Notifications_IsRecalled DEFAULT (0)"));
            mb.Sql(AddColumnIfMissing("Notifications", "RecalledAt",        "datetime2 NULL"));
            mb.Sql(AddColumnIfMissing("Notifications", "RecalledByUserId",  "nvarchar(max) NULL"));
            mb.Sql(AddColumnIfMissing("Notifications", "ScheduleAt",        "datetime2 NULL"));
            mb.Sql(AddColumnIfMissing("Notifications", "TargetRolesCsv",    "nvarchar(max) NULL"));
            mb.Sql(AddColumnIfMissing("Notifications", "TargetUserIdsCsv",  "nvarchar(max) NULL"));

            // Backfill existing notifications with sensible delivery state.
            mb.Sql(@"
UPDATE [dbo].[Notifications]
SET DeliveryStatus = N'Delivered',
    DeliveredAt    = ISNULL(DeliveredAt, CreatedAt)
WHERE DeliveryStatus IS NULL OR DeliveryStatus = N'';");

            // ── AspNetUsers ──────────────────────────────────────────────────
            mb.Sql(AddColumnIfMissing("AspNetUsers", "LastSeenAt", "datetime2 NULL"));

            // ── NotificationTemplates table ──────────────────────────────────
            mb.Sql(@"
IF OBJECT_ID(N'[dbo].[NotificationTemplates]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NotificationTemplates] (
        [Id]              int IDENTITY(1,1) NOT NULL,
        [Name]            nvarchar(120)     NOT NULL,
        [Title]           nvarchar(200)     NOT NULL,
        [Body]            nvarchar(max)     NOT NULL,
        [Type]            nvarchar(40)      NOT NULL,
        [Severity]        nvarchar(20)      NOT NULL,
        [Link]            nvarchar(max)     NULL,
        [CreatedByUserId] nvarchar(max)     NULL,
        [CreatedAt]       datetime2         NOT NULL,
        [UpdatedAt]       datetime2         NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([Id])
    );
END");

            // ── PlanChangeLogs table ─────────────────────────────────────────
            mb.Sql(@"
IF OBJECT_ID(N'[dbo].[PlanChangeLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PlanChangeLogs] (
        [Id]                    int IDENTITY(1,1) NOT NULL,
        [OrganizationId]        int               NOT NULL,
        [FromPlan]              nvarchar(max)     NULL,
        [ToPlan]                nvarchar(max)     NOT NULL,
        [FromPurchasedLicenses] int               NULL,
        [ToPurchasedLicenses]   int               NOT NULL,
        [FromLicenseEndsAt]     datetime2         NULL,
        [ToLicenseEndsAt]       datetime2         NULL,
        [ChangeType]            nvarchar(max)     NOT NULL,
        [Reason]                nvarchar(max)     NULL,
        [ChangedByUserId]       nvarchar(max)     NULL,
        [ChangedByEmail]        nvarchar(max)     NULL,
        [CreatedAt]             datetime2         NOT NULL,
        CONSTRAINT [PK_PlanChangeLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanChangeLogs_Organizations_OrganizationId]
            FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_PlanChangeLogs_OrganizationId]
        ON [dbo].[PlanChangeLogs]([OrganizationId]);
END");
        }

        protected override void Down(MigrationBuilder mb)
        {
            mb.Sql("IF OBJECT_ID(N'[dbo].[PlanChangeLogs]', N'U') IS NOT NULL DROP TABLE [dbo].[PlanChangeLogs];");
            mb.Sql("IF OBJECT_ID(N'[dbo].[NotificationTemplates]', N'U') IS NOT NULL DROP TABLE [dbo].[NotificationTemplates];");

            string[][] drops = new[]
            {
                new[]{"UserNotifications","ClickedAt"}, new[]{"UserNotifications","EmailSent"}, new[]{"UserNotifications","IsClicked"},
                new[]{"PaymentRecords","DueDate"},
                new[]{"Organizations","AutoRenew"}, new[]{"Organizations","LicenseEndsAt"}, new[]{"Organizations","LicenseNotes"}, new[]{"Organizations","LicenseStartsAt"},
                new[]{"Notifications","DeliveredAt"}, new[]{"Notifications","DeliveryStatus"}, new[]{"Notifications","IsRecalled"},
                new[]{"Notifications","RecalledAt"}, new[]{"Notifications","RecalledByUserId"}, new[]{"Notifications","ScheduleAt"},
                new[]{"Notifications","TargetRolesCsv"}, new[]{"Notifications","TargetUserIdsCsv"},
                new[]{"AspNetUsers","LastSeenAt"},
            };
            foreach (var d in drops)
            {
                mb.Sql($"IF COL_LENGTH(N'[dbo].[{d[0]}]', N'{d[1]}') IS NOT NULL ALTER TABLE [dbo].[{d[0]}] DROP COLUMN [{d[1]}];");
            }
        }
    }
}
