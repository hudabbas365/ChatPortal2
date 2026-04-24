using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseTermsToOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRenew",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseEndsAt",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNotes",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseStartsAt",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    FromPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToPlan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromPurchasedLicenses = table.Column<int>(type: "int", nullable: true),
                    ToPurchasedLicenses = table.Column<int>(type: "int", nullable: false),
                    FromLicenseEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ToLicenseEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanChangeLogs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanChangeLogs_OrganizationId",
                table: "PlanChangeLogs",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanChangeLogs");

            migrationBuilder.DropColumn(
                name: "AutoRenew",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LicenseEndsAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LicenseNotes",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LicenseStartsAt",
                table: "Organizations");
        }
    }
}
