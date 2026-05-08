IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417083551_AddPayPalSubscriptionFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417083551_AddPayPalSubscriptionFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [BlockedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [BlockedReason] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsBlocked] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PayPalPlanId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PayPalSubscriptionId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [SubscriptionNextBillingDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [SubscriptionStartDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    ALTER TABLE [Organizations] ADD [SubscriptionStatus] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    CREATE TABLE [PaymentRecords] (
        [Id] int NOT NULL IDENTITY,
        [OrganizationId] int NOT NULL,
        [UserId] nvarchar(max) NULL,
        [PaymentType] nvarchar(max) NOT NULL,
        [PaymentMethod] nvarchar(max) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [PayPalOrderId] nvarchar(max) NULL,
        [PayPalSubscriptionId] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [PlanKey] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PaymentRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentRecords_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    CREATE INDEX [IX_PaymentRecords_OrganizationId] ON [PaymentRecords] ([OrganizationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090629_test11'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417090629_test11', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417090817_test12'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417090817_test12', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417211937_AddEmailVerificationFields'
)
BEGIN
    ALTER TABLE [Organizations] ADD [EmailVerificationToken] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417211937_AddEmailVerificationFields'
)
BEGIN
    ALTER TABLE [Organizations] ADD [EmailVerificationTokenExpiry] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417211937_AddEmailVerificationFields'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsEmailVerified] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417211937_AddEmailVerificationFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417211937_AddEmailVerificationFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418104055_AddRestApiFieldsToDatasource'
)
BEGIN
    ALTER TABLE [Datasources] ADD [ApiKey] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418104055_AddRestApiFieldsToDatasource'
)
BEGIN
    ALTER TABLE [Datasources] ADD [ApiUrl] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418104055_AddRestApiFieldsToDatasource'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260418104055_AddRestApiFieldsToDatasource', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418195752_test14'
)
BEGIN
    ALTER TABLE [Datasources] ADD [ApiMethod] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418195752_test14'
)
BEGIN
    CREATE TABLE [SharedReports] (
        [Id] int NOT NULL IDENTITY,
        [ReportId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [SharedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SharedReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SharedReports_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SharedReports_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418195752_test14'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SharedReports_ReportId_UserId] ON [SharedReports] ([ReportId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418195752_test14'
)
BEGIN
    CREATE INDEX [IX_SharedReports_UserId] ON [SharedReports] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260418195752_test14'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260418195752_test14', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422224521_AddReportRevisions'
)
BEGIN
    CREATE TABLE [ReportRevisions] (
        [Id] int NOT NULL IDENTITY,
        [ReportId] int NOT NULL,
        [Kind] nvarchar(450) NOT NULL,
        [Name] nvarchar(max) NULL,
        [CanvasJson] nvarchar(max) NULL,
        [ReportName] nvarchar(max) NULL,
        [CreatedBy] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ReportRevisions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReportRevisions_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422224521_AddReportRevisions'
)
BEGIN
    CREATE INDEX [IX_ReportRevisions_ReportId_Kind_CreatedAt] ON [ReportRevisions] ([ReportId], [Kind], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260422224521_AddReportRevisions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260422224521_AddReportRevisions', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423200736_AddNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423200736_AddNotifications', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] int NOT NULL IDENTITY,
        [Scope] nvarchar(max) NOT NULL,
        [OrganizationId] int NULL,
        [TargetUserId] nvarchar(max) NULL,
        [Title] nvarchar(max) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [Severity] nvarchar(max) NOT NULL,
        [Link] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [CreatedByUserId] nvarchar(max) NULL,
        [CreatedByRole] nvarchar(max) NULL,
        [SystemKey] nvarchar(max) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    CREATE TABLE [UserNotifications] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [NotificationId] int NOT NULL,
        [ReadAt] datetime2 NULL,
        [DismissedAt] datetime2 NULL,
        CONSTRAINT [PK_UserNotifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNotifications_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserNotifications_Notifications_NotificationId] FOREIGN KEY ([NotificationId]) REFERENCES [Notifications] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    CREATE INDEX [IX_Notifications_OrganizationId] ON [Notifications] ([OrganizationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    CREATE INDEX [IX_UserNotifications_NotificationId] ON [UserNotifications] ([NotificationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    CREATE INDEX [IX_UserNotifications_UserId] ON [UserNotifications] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260423203014_test100'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423203014_test100', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424204124_AddPerTypeLicenses'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PurchasedEnterpriseLicenses] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424204124_AddPerTypeLicenses'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PurchasedProfessionalLicenses] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424204124_AddPerTypeLicenses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424204124_AddPerTypeLicenses', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingAddressLine1] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingAddressLine2] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingCity] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingCompany] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingCountry] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingEmail] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingPostalCode] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [BillingState] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [CaptureId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [InvoiceNumber] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [LineItemsJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [PaidAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [PayerEmail] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [PayerName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [PdfPath] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [Quantity] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [Subtotal] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [TaxAmount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [TaxRatePercent] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [TaxRegion] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [TokensAdded] bigint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [UnitPrice] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [Organizations] ADD [FailedPaymentCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    ALTER TABLE [Organizations] ADD [GraceUntil] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    CREATE TABLE [SupportTickets] (
        [Id] int NOT NULL IDENTITY,
        [TicketNumber] nvarchar(40) NOT NULL,
        [OrganizationId] int NULL,
        [UserId] nvarchar(max) NULL,
        [RequesterName] nvarchar(160) NOT NULL,
        [RequesterEmail] nvarchar(200) NOT NULL,
        [Category] nvarchar(40) NOT NULL,
        [Priority] nvarchar(20) NOT NULL,
        [Subject] nvarchar(250) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ResolvedAt] datetime2 NULL,
        [AssignedToUserId] nvarchar(450) NULL,
        [Response] nvarchar(max) NULL,
        CONSTRAINT [PK_SupportTickets] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424214044_PayPal_GracePeriod_Columns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424214044_PayPal_GracePeriod_Columns', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222422_AddLastSeenAt'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [LastSeenAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222422_AddLastSeenAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424222422_AddLastSeenAt', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    ALTER TABLE [Organizations] ADD [AutoRenew] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    ALTER TABLE [Organizations] ADD [LicenseEndsAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    ALTER TABLE [Organizations] ADD [LicenseNotes] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    ALTER TABLE [Organizations] ADD [LicenseStartsAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    CREATE TABLE [PlanChangeLogs] (
        [Id] int NOT NULL IDENTITY,
        [OrganizationId] int NOT NULL,
        [FromPlan] nvarchar(max) NULL,
        [ToPlan] nvarchar(max) NOT NULL,
        [FromPurchasedLicenses] int NULL,
        [ToPurchasedLicenses] int NOT NULL,
        [FromLicenseEndsAt] datetime2 NULL,
        [ToLicenseEndsAt] datetime2 NULL,
        [ChangeType] nvarchar(max) NOT NULL,
        [Reason] nvarchar(max) NULL,
        [ChangedByUserId] nvarchar(max) NULL,
        [ChangedByEmail] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PlanChangeLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanChangeLogs_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    CREATE INDEX [IX_PlanChangeLogs_OrganizationId] ON [PlanChangeLogs] ([OrganizationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424222538_AddLicenseTermsToOrganization'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424222538_AddLicenseTermsToOrganization', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [LastLoginAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [LastLoginCity] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [LastLoginCountry] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [LastLoginIp] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    CREATE TABLE [DigestRuns] (
        [Id] int NOT NULL IDENTITY,
        [RunWeekIso] nvarchar(max) NOT NULL,
        [SentAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DigestRuns] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    CREATE TABLE [IntegrationHealthChecks] (
        [Id] int NOT NULL IDENTITY,
        [Provider] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [LatencyMs] int NOT NULL,
        [Error] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IntegrationHealthChecks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260424223512_AddSecurityAndOpsFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424223512_AddSecurityAndOpsFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[UserNotifications]', N'ClickedAt') IS NULL
       ALTER TABLE [dbo].[UserNotifications] ADD [ClickedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[UserNotifications]', N'EmailSent') IS NULL
       ALTER TABLE [dbo].[UserNotifications] ADD [EmailSent] bit NOT NULL CONSTRAINT DF_UserNotifications_EmailSent DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[UserNotifications]', N'IsClicked') IS NULL
       ALTER TABLE [dbo].[UserNotifications] ADD [IsClicked] bit NOT NULL CONSTRAINT DF_UserNotifications_IsClicked DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[PaymentRecords]', N'DueDate') IS NULL
       ALTER TABLE [dbo].[PaymentRecords] ADD [DueDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Organizations]', N'AutoRenew') IS NULL
       ALTER TABLE [dbo].[Organizations] ADD [AutoRenew] bit NOT NULL CONSTRAINT DF_Organizations_AutoRenew DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Organizations]', N'LicenseEndsAt') IS NULL
       ALTER TABLE [dbo].[Organizations] ADD [LicenseEndsAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Organizations]', N'LicenseNotes') IS NULL
       ALTER TABLE [dbo].[Organizations] ADD [LicenseNotes] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Organizations]', N'LicenseStartsAt') IS NULL
       ALTER TABLE [dbo].[Organizations] ADD [LicenseStartsAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'DeliveredAt') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [DeliveredAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'DeliveryStatus') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [DeliveryStatus] nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_DeliveryStatus DEFAULT (N'Delivered');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'IsRecalled') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [IsRecalled] bit NOT NULL CONSTRAINT DF_Notifications_IsRecalled DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'RecalledAt') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [RecalledAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'RecalledByUserId') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [RecalledByUserId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'ScheduleAt') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [ScheduleAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'TargetRolesCsv') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [TargetRolesCsv] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[Notifications]', N'TargetUserIdsCsv') IS NULL
       ALTER TABLE [dbo].[Notifications] ADD [TargetUserIdsCsv] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    UPDATE [dbo].[Notifications]
    SET DeliveryStatus = N'Delivered',
        DeliveredAt    = ISNULL(DeliveredAt, CreatedAt)
    WHERE DeliveryStatus IS NULL OR DeliveryStatus = N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'LastSeenAt') IS NULL
       ALTER TABLE [dbo].[AspNetUsers] ADD [LastSeenAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
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
    END
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
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
    END
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425170428_ApplyMissingNotificationAndInvoiceColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260425170428_ApplyMissingNotificationAndInvoiceColumns', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260428075601_AddOrganizationGuid'
)
BEGIN
    ALTER TABLE [Organizations] ADD [OrganizationGuid] uniqueidentifier NOT NULL DEFAULT (NEWID());
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260428075601_AddOrganizationGuid'
)
BEGIN
    UPDATE Organizations SET OrganizationGuid = NEWID();
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260428075601_AddOrganizationGuid'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Organizations_OrganizationGuid] ON [Organizations] ([OrganizationGuid]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260428075601_AddOrganizationGuid'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260428075601_AddOrganizationGuid', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260429122927_AddLicenseSubscriptionFields'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PayPalEntSubscriptionId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260429122927_AddLicenseSubscriptionFields'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PayPalProSubscriptionId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260429122927_AddLicenseSubscriptionFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260429122927_AddLicenseSubscriptionFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260501132027_AddReportEmbedTokenVersion'
)
BEGIN
    ALTER TABLE [Reports] ADD [EmbedTokenVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260501132027_AddReportEmbedTokenVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260501132027_AddReportEmbedTokenVersion', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260504202344_AddPayPalEventIdIdempotency'
)
BEGIN
    ALTER TABLE [PaymentRecords] ADD [PayPalEventId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260504202344_AddPayPalEventIdIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentRecords_PayPalEventId] ON [PaymentRecords] ([PayPalEventId]) WHERE [PayPalEventId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260504202344_AddPayPalEventIdIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260504202344_AddPayPalEventIdIdempotency', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260507215818_AddUniqueOrganizationName'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'Name');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Organizations] ALTER COLUMN [Name] nvarchar(450) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260507215818_AddUniqueOrganizationName'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Organizations_Name_Unique] ON [Organizations] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260507215818_AddUniqueOrganizationName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260507215818_AddUniqueOrganizationName', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508094456_AddDatasourceTransformToml'
)
BEGIN
    ALTER TABLE [Datasources] ADD [TransformEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508094456_AddDatasourceTransformToml'
)
BEGIN
    ALTER TABLE [Datasources] ADD [TransformToml] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508094456_AddDatasourceTransformToml'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260508094456_AddDatasourceTransformToml', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    ALTER TABLE [Workspaces] ADD [Type] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE TABLE [TransformDrafts] (
        [Id] int NOT NULL IDENTITY,
        [Guid] nvarchar(450) NOT NULL,
        [DatasourceId] int NULL,
        [DatasourceGuidsCsv] nvarchar(max) NULL,
        [Name] nvarchar(max) NOT NULL,
        [TomlDefinition] nvarchar(max) NOT NULL,
        [StepsJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [CreatedByUserId] nvarchar(max) NULL,
        [UpdatedByUserId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [PublishedAt] datetime2 NULL,
        [AuditMetadataJson] nvarchar(max) NULL,
        CONSTRAINT [PK_TransformDrafts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransformDrafts_Datasources_DatasourceId] FOREIGN KEY ([DatasourceId]) REFERENCES [Datasources] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE TABLE [TransformRunAudits] (
        [Id] int NOT NULL IDENTITY,
        [RunGuid] nvarchar(450) NOT NULL,
        [DatasourceId] int NULL,
        [TransformDraftId] int NULL,
        [Success] bit NOT NULL,
        [InputRowCount] int NOT NULL,
        [OutputRowCount] int NOT NULL,
        [DurationMs] bigint NOT NULL,
        [MessagesJson] nvarchar(max) NULL,
        [Error] nvarchar(max) NULL,
        [CreatedByUserId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TransformRunAudits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransformRunAudits_Datasources_DatasourceId] FOREIGN KEY ([DatasourceId]) REFERENCES [Datasources] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TransformRunAudits_TransformDrafts_TransformDraftId] FOREIGN KEY ([TransformDraftId]) REFERENCES [TransformDrafts] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE TABLE [TransformSources] (
        [Id] int NOT NULL IDENTITY,
        [TransformDraftId] int NOT NULL,
        [DatasourceId] int NOT NULL,
        [Alias] nvarchar(450) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_TransformSources] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransformSources_Datasources_DatasourceId] FOREIGN KEY ([DatasourceId]) REFERENCES [Datasources] ([Id]),
        CONSTRAINT [FK_TransformSources_TransformDrafts_TransformDraftId] FOREIGN KEY ([TransformDraftId]) REFERENCES [TransformDrafts] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE TABLE [TransformSteps] (
        [Id] int NOT NULL IDENTITY,
        [TransformDraftId] int NOT NULL,
        [StepGuid] nvarchar(max) NOT NULL,
        [StepType] nvarchar(max) NOT NULL,
        [Title] nvarchar(max) NULL,
        [ParametersJson] nvarchar(max) NULL,
        [Enabled] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TransformSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransformSteps_TransformDrafts_TransformDraftId] FOREIGN KEY ([TransformDraftId]) REFERENCES [TransformDrafts] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE INDEX [IX_TransformDrafts_DatasourceId] ON [TransformDrafts] ([DatasourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TransformDrafts_Guid] ON [TransformDrafts] ([Guid]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE INDEX [IX_TransformRunAudits_DatasourceId] ON [TransformRunAudits] ([DatasourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TransformRunAudits_RunGuid] ON [TransformRunAudits] ([RunGuid]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE INDEX [IX_TransformRunAudits_TransformDraftId] ON [TransformRunAudits] ([TransformDraftId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE INDEX [IX_TransformSources_DatasourceId] ON [TransformSources] ([DatasourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TransformSources_TransformDraftId_DatasourceId_Alias] ON [TransformSources] ([TransformDraftId], [DatasourceId], [Alias]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    CREATE INDEX [IX_TransformSteps_TransformDraftId_SortOrder] ON [TransformSteps] ([TransformDraftId], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260508114102_AddWorkspaceType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260508114102_AddWorkspaceType', N'8.0.0');
END;
GO

COMMIT;
GO

