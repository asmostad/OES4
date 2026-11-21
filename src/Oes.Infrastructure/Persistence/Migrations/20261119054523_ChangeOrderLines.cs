using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsItemized",
                table: "change_orders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "change_order_lines",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ChangeOrderId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PriceListItemId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ProductId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Code = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_change_order_lines_change_orders_ChangeOrderId",
                        column: x => x.ChangeOrderId,
                        principalTable: "change_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_change_order_lines_ChangeOrderId",
                table: "change_order_lines",
                column: "ChangeOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_change_order_lines_TenantId_ChangeOrderId_Sort",
                table: "change_order_lines",
                columns: new[] { "TenantId", "ChangeOrderId", "Sort" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_order_lines");

            migrationBuilder.DropColumn(
                name: "IsItemized",
                table: "change_orders");
        }
    }
}
