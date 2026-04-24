using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class PayPal_GracePeriod_Columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingAddressLine1",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddressLine2",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCity",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCompany",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCountry",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingEmail",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingName",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingPostalCode",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineItemsJson",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "PaymentRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerEmail",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerName",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfPath",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "PaymentRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "PaymentRecords",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "PaymentRecords",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "PaymentRecords",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegion",
                table: "PaymentRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TokensAdded",
                table: "PaymentRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "PaymentRecords",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedPaymentCount",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GraceUntil",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequesterName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequesterEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedToUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Response = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "BillingAddressLine1",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingAddressLine2",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingCity",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingCompany",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingCountry",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingEmail",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingName",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingPostalCode",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "LineItemsJson",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PayerEmail",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PayerName",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PdfPath",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "TaxRegion",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "TokensAdded",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "FailedPaymentCount",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "GraceUntil",
                table: "Organizations");
        }
    }
}
