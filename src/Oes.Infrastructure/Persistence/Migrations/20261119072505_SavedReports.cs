using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavedReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_reports",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    RangeKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    From = table.Column<DateTime>(type: "date", nullable: true),
                    To = table.Column<DateTime>(type: "date", nullable: true),
                    OwnerFilterId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Schedule = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ScheduleDay = table.Column<int>(type: "int", nullable: false),
                    NextSendAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_reports", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_saved_reports_NextSendAt",
                table: "saved_reports",
                column: "NextSendAt");

            migrationBuilder.CreateIndex(
                name: "IX_saved_reports_TenantId_UserId",
                table: "saved_reports",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_reports");
        }
    }
}
