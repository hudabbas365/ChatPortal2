using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    public partial class AddOrganizationSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Organizations");
        }
    }
}
