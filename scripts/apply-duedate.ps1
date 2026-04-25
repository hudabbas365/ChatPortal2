$ErrorActionPreference = 'Stop'
$dll = 'C:\Users\hussa\source\repos\ChatPortal2\bin\Debug\net8.0\Microsoft.Data.SqlClient.dll'
Add-Type -Path $dll
$cs = 'Server=tcp:SQL8012.site4now.net,1433;Database=db_ac82d6_hudabbas;User Id=db_ac82d6_hudabbas_admin;Password=shadowfax$V3;Encrypt=True;TrustServerCertificate=True'
$c = New-Object Microsoft.Data.SqlClient.SqlConnection $cs
$c.Open()
$cmd = $c.CreateCommand()

$cmd.CommandText = "IF COL_LENGTH('PaymentRecords','DueDate') IS NULL ALTER TABLE PaymentRecords ADD DueDate datetime2 NULL;"
[void]$cmd.ExecuteNonQuery()
Write-Host "DueDate column ensured."

$cmd.CommandText = @"
UPDATE PaymentRecords
SET InvoiceNumber = CONCAT('INV-', FORMAT(CreatedAt,'yyyyMM'), '-', RIGHT(REPLICATE('0',6)+CAST(Id AS NVARCHAR(6)),6))
WHERE InvoiceNumber IS NULL OR InvoiceNumber = '';
"@
$rows = $cmd.ExecuteNonQuery()
Write-Host "Backfilled InvoiceNumber for $rows row(s)."

$cmd.CommandText = "IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId='20260424220000_AddInvoiceMetadataToPaymentRecord') INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260424220000_AddInvoiceMetadataToPaymentRecord','8.0.0');"
[void]$cmd.ExecuteNonQuery()
Write-Host "Migration history recorded."

$c.Close()
Write-Host "Done."
