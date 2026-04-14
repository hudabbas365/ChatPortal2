using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    public partial class AddOrgPlanColumnsV2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Organizations', 'Plan') IS NULL
    ALTER TABLE [Organizations] ADD [Plan] int NOT NULL CONSTRAINT [DF_Organizations_Plan] DEFAULT(0);
IF COL_LENGTH('Organizations', 'EnterpriseExtraTokenPacks') IS NULL
    ALTER TABLE [Organizations] ADD [EnterpriseExtraTokenPacks] int NOT NULL CONSTRAINT [DF_Organizations_EnterpriseExtraTokenPacks] DEFAULT(0);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Organizations', 'EnterpriseExtraTokenPacks') IS NOT NULL
    ALTER TABLE [Organizations] DROP COLUMN [EnterpriseExtraTokenPacks];
IF COL_LENGTH('Organizations', 'Plan') IS NOT NULL
    ALTER TABLE [Organizations] DROP COLUMN [Plan];");
        }
    }
}
