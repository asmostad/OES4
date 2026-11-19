using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderNotes",
                table: "project_trades",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderSupplier",
                table: "project_trades",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderTakeoffJson",
                table: "project_trades",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrderedAt",
                table: "project_trades",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "OrderedByUserId",
                table: "project_trades",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_sheet_lines",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ProjectTradeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ProductId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    TakeoffRuleId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Sku = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_sheet_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_sheet_lines_project_trades_ProjectTradeId",
                        column: x => x.ProjectTradeId,
                        principalTable: "project_trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_order_sheet_lines_ProjectTradeId",
                table: "order_sheet_lines",
                column: "ProjectTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_order_sheet_lines_TenantId_ProjectTradeId_Sort",
                table: "order_sheet_lines",
                columns: new[] { "TenantId", "ProjectTradeId", "Sort" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_sheet_lines");

            migrationBuilder.DropColumn(
                name: "OrderNotes",
                table: "project_trades");

            migrationBuilder.DropColumn(
                name: "OrderSupplier",
                table: "project_trades");

            migrationBuilder.DropColumn(
                name: "OrderTakeoffJson",
                table: "project_trades");

            migrationBuilder.DropColumn(
                name: "OrderedAt",
                table: "project_trades");

            migrationBuilder.DropColumn(
                name: "OrderedByUserId",
                table: "project_trades");
        }
    }
}
