using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatPortal2.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgPlanColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Plan",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnterpriseExtraTokenPacks",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Drop the old MonthlyTokenBudget column as it's now a computed property
            migrationBuilder.DropColumn(
                name: "MonthlyTokenBudget",
                table: "Organizations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "EnterpriseExtraTokenPacks",
                table: "Organizations");

            migrationBuilder.AddColumn<int>(
                name: "MonthlyTokenBudget",
                table: "Organizations",
                type: "int",
                nullable: false,
                defaultValue: 2000000);
        }
    }
}
