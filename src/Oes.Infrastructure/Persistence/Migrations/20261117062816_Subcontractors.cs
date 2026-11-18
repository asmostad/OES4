using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Subcontractors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_access_tokens",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_access_tokens", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subcontractors",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeCodes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    InsuranceExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subcontractors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractors_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subcontractor_documents",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SubcontractorId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    DocTypeKey = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    DocumentId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subcontractor_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_documents_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subcontractor_documents_subcontractors_SubcontractorId",
                        column: x => x.SubcontractorId,
                        principalTable: "subcontractors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "subcontractor_rates",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SubcontractorId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subcontractor_rates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subcontractor_rates_subcontractors_SubcontractorId",
                        column: x => x.SubcontractorId,
                        principalTable: "subcontractors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ProjectId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ProjectTradeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SubcontractorId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AcceptStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AcceptedName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeclineReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    WorkDate = table.Column<DateTime>(type: "date", nullable: true),
                    ArrivalTime = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletionNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_orders_project_trades_ProjectTradeId",
                        column: x => x.ProjectTradeId,
                        principalTable: "project_trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_orders_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_orders_subcontractors_SubcontractorId",
                        column: x => x.SubcontractorId,
                        principalTable: "subcontractors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "work_order_lines",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    WorkOrderId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    RateId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_lines_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_external_access_tokens_TenantId_EntityType_EntityId",
                table: "external_access_tokens",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_external_access_tokens_TokenHash",
                table: "external_access_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_documents_DocumentId",
                table: "subcontractor_documents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_documents_SubcontractorId",
                table: "subcontractor_documents",
                column: "SubcontractorId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_documents_TenantId_SubcontractorId",
                table: "subcontractor_documents",
                columns: new[] { "TenantId", "SubcontractorId" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_rates_SubcontractorId",
                table: "subcontractor_rates",
                column: "SubcontractorId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractor_rates_TenantId_SubcontractorId",
                table: "subcontractor_rates",
                columns: new[] { "TenantId", "SubcontractorId" });

            migrationBuilder.CreateIndex(
                name: "IX_subcontractors_ContactId",
                table: "subcontractors",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_subcontractors_TenantId_ContactId",
                table: "subcontractors",
                columns: new[] { "TenantId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_order_lines_TenantId_WorkOrderId",
                table: "work_order_lines",
                columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_lines_WorkOrderId",
                table: "work_order_lines",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_ProjectId",
                table: "work_orders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_ProjectTradeId",
                table: "work_orders",
                column: "ProjectTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_SubcontractorId",
                table: "work_orders",
                column: "SubcontractorId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_TenantId_Number",
                table: "work_orders",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_TenantId_ProjectTradeId",
                table: "work_orders",
                columns: new[] { "TenantId", "ProjectTradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_TenantId_SubcontractorId",
                table: "work_orders",
                columns: new[] { "TenantId", "SubcontractorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_access_tokens");

            migrationBuilder.DropTable(
                name: "subcontractor_documents");

            migrationBuilder.DropTable(
                name: "subcontractor_rates");

            migrationBuilder.DropTable(
                name: "work_order_lines");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.DropTable(
                name: "subcontractors");
        }
    }
}
