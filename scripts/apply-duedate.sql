IF COL_LENGTH('PaymentRecords','DueDate') IS NULL
    ALTER TABLE PaymentRecords ADD DueDate datetime2 NULL;
GO

UPDATE PaymentRecords
SET InvoiceNumber = CONCAT('INV-', FORMAT(CreatedAt,'yyyyMM'), '-', RIGHT(REPLICATE('0',6)+CAST(Id AS NVARCHAR(6)),6))
WHERE InvoiceNumber IS NULL OR InvoiceNumber = '';
GO

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId='20260424220000_AddInvoiceMetadataToPaymentRecord')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260424220000_AddInvoiceMetadataToPaymentRecord','8.0.0');
GO
