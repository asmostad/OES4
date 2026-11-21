using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_rules",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Trigger = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    StageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DueInDays = table.Column<int>(type: "int", nullable: true),
                    AssigneeUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    SequenceId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rules", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "automation_runs",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    RuleId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StageChangeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ItemId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_runs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_TenantId_Trigger_StageId",
                table: "automation_rules",
                columns: new[] { "TenantId", "Trigger", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_RuleId_StageChangeId",
                table: "automation_runs",
                columns: new[] { "RuleId", "StageChangeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_TenantId_CreatedAt",
                table: "automation_runs",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_rules");

            migrationBuilder.DropTable(
                name: "automation_runs");
        }
    }
}
