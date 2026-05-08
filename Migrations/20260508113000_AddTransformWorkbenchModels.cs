using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInsights.Migrations
{
    public partial class AddTransformWorkbenchModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransformDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Guid = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DatasourceId = table.Column<int>(type: "int", nullable: true),
                    DatasourceGuidsCsv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TomlDefinition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransformDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransformDrafts_Datasources_DatasourceId",
                        column: x => x.DatasourceId,
                        principalTable: "Datasources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TransformRunAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunGuid = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DatasourceId = table.Column<int>(type: "int", nullable: true),
                    TransformDraftId = table.Column<int>(type: "int", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    InputRowCount = table.Column<int>(type: "int", nullable: false),
                    OutputRowCount = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    MessagesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransformRunAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransformRunAudits_Datasources_DatasourceId",
                        column: x => x.DatasourceId,
                        principalTable: "Datasources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransformRunAudits_TransformDrafts_TransformDraftId",
                        column: x => x.TransformDraftId,
                        principalTable: "TransformDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TransformSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransformDraftId = table.Column<int>(type: "int", nullable: false),
                    DatasourceId = table.Column<int>(type: "int", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransformSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransformSources_Datasources_DatasourceId",
                        column: x => x.DatasourceId,
                        principalTable: "Datasources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_TransformSources_TransformDrafts_TransformDraftId",
                        column: x => x.TransformDraftId,
                        principalTable: "TransformDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransformSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransformDraftId = table.Column<int>(type: "int", nullable: false),
                    StepGuid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StepType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransformSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransformSteps_TransformDrafts_TransformDraftId",
                        column: x => x.TransformDraftId,
                        principalTable: "TransformDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransformDrafts_DatasourceId",
                table: "TransformDrafts",
                column: "DatasourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TransformDrafts_Guid",
                table: "TransformDrafts",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransformRunAudits_DatasourceId",
                table: "TransformRunAudits",
                column: "DatasourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TransformRunAudits_RunGuid",
                table: "TransformRunAudits",
                column: "RunGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransformRunAudits_TransformDraftId",
                table: "TransformRunAudits",
                column: "TransformDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_TransformSources_DatasourceId",
                table: "TransformSources",
                column: "DatasourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TransformSources_TransformDraftId_DatasourceId_Alias",
                table: "TransformSources",
                columns: new[] { "TransformDraftId", "DatasourceId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransformSteps_TransformDraftId_SortOrder",
                table: "TransformSteps",
                columns: new[] { "TransformDraftId", "SortOrder" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TransformRunAudits");
            migrationBuilder.DropTable(name: "TransformSources");
            migrationBuilder.DropTable(name: "TransformSteps");
            migrationBuilder.DropTable(name: "TransformDrafts");
        }
    }
}
