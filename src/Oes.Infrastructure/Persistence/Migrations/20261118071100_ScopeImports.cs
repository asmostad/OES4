using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scope_imports",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EstimateId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    DocumentId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Carrier = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    CarrierPriceList = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    ExtractedJson = table.Column<string>(type: "mediumtext", nullable: false),
                    LinesJson = table.Column<string>(type: "mediumtext", nullable: false),
                    ImportedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scope_imports", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scope_imports_TenantId_EstimateId",
                table: "scope_imports",
                columns: new[] { "TenantId", "EstimateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scope_imports");
        }
    }
}
