using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Production : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_stage_changes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    FromStageId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ToStageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    IsAutomatic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_stage_changes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "workflow_stages",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Level = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    StaleAfterDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_stages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    EstimateId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    EstimateVersionId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ClaimId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    OwnerUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    StageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StageChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OriginalContract = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ApprovedChangeOrders = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovedFromBoardAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovalReasonKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    RemovalNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_estimate_versions_EstimateVersionId",
                        column: x => x.EstimateVersionId,
                        principalTable: "estimate_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_workflow_stages_StageId",
                        column: x => x.StageId,
                        principalTable: "workflow_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "change_orders",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ProjectId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ProjectTradeId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IsInsuranceSupplement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ApprovedByName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_change_orders_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_trades",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ProjectId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    StageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StageChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MaterialDate = table.Column<DateTime>(type: "date", nullable: true),
                    WorkDate = table.Column<DateTime>(type: "date", nullable: true),
                    CrewNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovedFromBoardAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemovalReasonKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    RemovalNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_trades_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_trades_trade_types_TradeTypeId",
                        column: x => x.TradeTypeId,
                        principalTable: "trade_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_trades_workflow_stages_StageId",
                        column: x => x.StageId,
                        principalTable: "workflow_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_change_orders_ProjectId",
                table: "change_orders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_change_orders_TenantId_ProjectId_Sequence",
                table: "change_orders",
                columns: new[] { "TenantId", "ProjectId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_trades_ProjectId",
                table: "project_trades",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_trades_StageId",
                table: "project_trades",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_project_trades_TenantId_ProjectId",
                table: "project_trades",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_trades_TenantId_StageId",
                table: "project_trades",
                columns: new[] { "TenantId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_project_trades_TradeTypeId",
                table: "project_trades",
                column: "TradeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_EstimateId",
                table: "projects",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_EstimateVersionId",
                table: "projects",
                column: "EstimateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_OpportunityId",
                table: "projects",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_PropertyId",
                table: "projects",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_StageId",
                table: "projects",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_EstimateId",
                table: "projects",
                columns: new[] { "TenantId", "EstimateId" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_Number",
                table: "projects",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_OpportunityId",
                table: "projects",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_TenantId_StageId",
                table: "projects",
                columns: new[] { "TenantId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_stage_changes_TenantId_EntityType_EntityId",
                table: "workflow_stage_changes",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_stages_TenantId_Level_Sort",
                table: "workflow_stages",
                columns: new[] { "TenantId", "Level", "Sort" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_orders");

            migrationBuilder.DropTable(
                name: "project_trades");

            migrationBuilder.DropTable(
                name: "workflow_stage_changes");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "workflow_stages");
        }
    }
}
