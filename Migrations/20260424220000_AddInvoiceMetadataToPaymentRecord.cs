using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceMetadataToPaymentRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "PaymentRecords",
                type: "datetime2",
                nullable: true);

            // Backfill InvoiceNumber for existing rows where it is null.
            // Format: INV-{yyyyMM}-{Id zero-padded to 6 digits}
            migrationBuilder.Sql(@"
                UPDATE PaymentRecords
                SET InvoiceNumber = CONCAT(
                    'INV-',
                    FORMAT(CreatedAt, 'yyyyMM'),
                    '-',
                    RIGHT(REPLICATE('0', 6) + CAST(Id AS NVARCHAR(6)), 6)
                )
                WHERE InvoiceNumber IS NULL OR InvoiceNumber = '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PaymentRecords");
        }
    }
}
