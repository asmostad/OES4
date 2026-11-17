using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Estimating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_categories",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_categories", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    RegionCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_lists", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Sku = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    DefaultCost = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    DefaultPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    IsMaterial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Supplier = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "catalog_items",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CategoryId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    CoverageNotes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalog_items_catalog_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "catalog_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "estimates",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ClaimId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    OwnerUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PriceListId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    OverheadPct = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    ProfitPct = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    TaxBasis = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ShowLinePrices = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Overhead = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Profit = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LatestVersionNo = table.Column<int>(type: "int", nullable: false),
                    SignedVersionId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_estimates_insurance_claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "insurance_claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_estimates_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_estimates_price_lists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_estimates_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "price_list_items",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PriceListId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CatalogItemId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Activity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    MaterialPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    LaborPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_list_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_price_list_items_catalog_items_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_list_items_price_lists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "price_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "estimate_sections",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EstimateId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    StructureId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    MeasurementSetId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ScopeOfWork = table.Column<string>(type: "text", nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimate_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_estimate_sections_estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_estimate_sections_measurement_sets_MeasurementSetId",
                        column: x => x.MeasurementSetId,
                        principalTable: "measurement_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "estimate_versions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EstimateId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IssuedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    RevisionReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Overhead = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Profit = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ContentJson = table.Column<string>(type: "json", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SignerName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimate_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_estimate_versions_estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "estimate_lines",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EstimateId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SectionId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CatalogItemId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    PriceListItemId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ProductId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Activity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    DefaultUnitPrice = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    IsMaterial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTaxable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ApprovalReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ApprovalRequestedBy = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ApprovalDecidedBy = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ApprovalDecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovalNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimate_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_estimate_lines_estimate_sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "estimate_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_categories_TenantId_Code",
                table: "catalog_categories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_items_CategoryId",
                table: "catalog_items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_items_TenantId_CategoryId_Code",
                table: "catalog_items",
                columns: new[] { "TenantId", "CategoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_items_TenantId_Code",
                table: "catalog_items",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_estimate_lines_SectionId",
                table: "estimate_lines",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_lines_TenantId_ApprovalStatus",
                table: "estimate_lines",
                columns: new[] { "TenantId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_estimate_lines_TenantId_EstimateId",
                table: "estimate_lines",
                columns: new[] { "TenantId", "EstimateId" });

            migrationBuilder.CreateIndex(
                name: "IX_estimate_sections_EstimateId",
                table: "estimate_sections",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_sections_MeasurementSetId",
                table: "estimate_sections",
                column: "MeasurementSetId");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_sections_TenantId_EstimateId",
                table: "estimate_sections",
                columns: new[] { "TenantId", "EstimateId" });

            migrationBuilder.CreateIndex(
                name: "IX_estimate_versions_EstimateId",
                table: "estimate_versions",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_versions_TenantId_EstimateId_VersionNo",
                table: "estimate_versions",
                columns: new[] { "TenantId", "EstimateId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_estimates_ClaimId",
                table: "estimates",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_estimates_OpportunityId",
                table: "estimates",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_estimates_PriceListId",
                table: "estimates",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_estimates_PropertyId",
                table: "estimates",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_estimates_TenantId_Number",
                table: "estimates",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_estimates_TenantId_OpportunityId",
                table: "estimates",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_estimates_TenantId_Status",
                table: "estimates",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_CatalogItemId",
                table: "price_list_items",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_PriceListId",
                table: "price_list_items",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_TenantId_PriceListId_CatalogItemId_Activity",
                table: "price_list_items",
                columns: new[] { "TenantId", "PriceListId", "CatalogItemId", "Activity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Sku",
                table: "products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estimate_lines");

            migrationBuilder.DropTable(
                name: "estimate_versions");

            migrationBuilder.DropTable(
                name: "price_list_items");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "estimate_sections");

            migrationBuilder.DropTable(
                name: "catalog_items");

            migrationBuilder.DropTable(
                name: "estimates");

            migrationBuilder.DropTable(
                name: "catalog_categories");

            migrationBuilder.DropTable(
                name: "price_lists");
        }
    }
}
