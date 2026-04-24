using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class AddPerTypeLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchasedEnterpriseLicenses",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchasedProfessionalLicenses",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchasedEnterpriseLicenses",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "PurchasedProfessionalLicenses",
                table: "Organizations");
        }
    }
}
