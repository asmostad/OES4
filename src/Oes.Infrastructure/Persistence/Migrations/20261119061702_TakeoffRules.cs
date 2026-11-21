using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TakeoffRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TakeoffMultiplier",
                table: "products",
                type: "decimal(14,6)",
                precision: 14,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakeoffSlot",
                table: "products",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakeoffJson",
                table: "estimate_sections",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TakeoffRuleId",
                table: "estimate_lines",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "takeoff_rules",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    ProductId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Slot = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Driver = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    WastePercent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false),
                    UseSuggestedWaste = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RoundMode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    RoundTo = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MinQty = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    MaxQty = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    Cond1Field = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Cond1Op = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: true),
                    Cond1Value = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    Cond2Field = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Cond2Op = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: true),
                    Cond2Value = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    OptionKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    OptionLabel = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    OptionDefaultOn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_takeoff_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_takeoff_rules_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_takeoff_rules_trade_types_TradeTypeId",
                        column: x => x.TradeTypeId,
                        principalTable: "trade_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_takeoff_rules_ProductId",
                table: "takeoff_rules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_takeoff_rules_TenantId_TradeTypeId_Sort",
                table: "takeoff_rules",
                columns: new[] { "TenantId", "TradeTypeId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_takeoff_rules_TradeTypeId",
                table: "takeoff_rules",
                column: "TradeTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "takeoff_rules");

            migrationBuilder.DropColumn(
                name: "TakeoffMultiplier",
                table: "products");

            migrationBuilder.DropColumn(
                name: "TakeoffSlot",
                table: "products");

            migrationBuilder.DropColumn(
                name: "TakeoffJson",
                table: "estimate_sections");

            migrationBuilder.DropColumn(
                name: "TakeoffRuleId",
                table: "estimate_lines");
        }
    }
}
