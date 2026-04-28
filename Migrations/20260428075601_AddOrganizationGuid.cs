using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationGuid",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            // Backfill: ensure every existing row has a distinct non-empty GUID.
            // The defaultValueSql above may assign the same NEWID() to all rows in
            // some execution plans, so we explicitly reassign each row a fresh value.
            migrationBuilder.Sql(
                "UPDATE Organizations SET OrganizationGuid = NEWID();");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OrganizationGuid",
                table: "Organizations",
                column: "OrganizationGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_OrganizationGuid",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "OrganizationGuid",
                table: "Organizations");
        }
    }
}
