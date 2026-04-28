using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalEventIdIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayPalEventId",
                table: "PaymentRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_PayPalEventId",
                table: "PaymentRecords",
                column: "PayPalEventId",
                unique: true,
                filter: "[PayPalEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_PayPalEventId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PayPalEventId",
                table: "PaymentRecords");
        }
    }
}
