using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Request = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_UserId_CreatedAt",
                table: "idempotency_records",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_UserId_Key",
                table: "idempotency_records",
                columns: new[] { "TenantId", "UserId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
