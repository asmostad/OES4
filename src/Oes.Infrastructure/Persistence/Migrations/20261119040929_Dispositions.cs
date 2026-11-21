using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InProspecting",
                table: "pipeline_stages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Existing companies: the starting stage (and the default "Contacted") feed the call list.
            migrationBuilder.Sql("UPDATE pipeline_stages SET InProspecting = 1 WHERE Outcome = 'open' AND (IsDefault = 1 OR Name = 'Contacted');");

            migrationBuilder.AddColumn<byte[]>(
                name: "LastDispositionId",
                table: "opportunities",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "opportunities",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LockedByUserId",
                table: "opportunities",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecycleAt",
                table: "opportunities",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dispositions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RecycleDays = table.Column<int>(type: "int", nullable: true),
                    MovesToStageId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    LostReasonKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    CountsAsAttempt = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDoNotContact = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispositions_pipeline_stages_MovesToStageId",
                        column: x => x.MovesToStageId,
                        principalTable: "pipeline_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "opportunity_dispositions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    DispositionId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_dispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_opportunity_dispositions_dispositions_DispositionId",
                        column: x => x.DispositionId,
                        principalTable: "dispositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunity_dispositions_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_LastDispositionId",
                table: "opportunities",
                column: "LastDispositionId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_Outcome_RecycleAt",
                table: "opportunities",
                columns: new[] { "TenantId", "Outcome", "RecycleAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dispositions_MovesToStageId",
                table: "dispositions",
                column: "MovesToStageId");

            migrationBuilder.CreateIndex(
                name: "IX_dispositions_TenantId_Sort",
                table: "dispositions",
                columns: new[] { "TenantId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_dispositions_DispositionId",
                table: "opportunity_dispositions",
                column: "DispositionId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_dispositions_OpportunityId",
                table: "opportunity_dispositions",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_dispositions_TenantId_ByUserId_CreatedAt",
                table: "opportunity_dispositions",
                columns: new[] { "TenantId", "ByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_dispositions_TenantId_OpportunityId",
                table: "opportunity_dispositions",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_dispositions_LastDispositionId",
                table: "opportunities",
                column: "LastDispositionId",
                principalTable: "dispositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_dispositions_LastDispositionId",
                table: "opportunities");

            migrationBuilder.DropTable(
                name: "opportunity_dispositions");

            migrationBuilder.DropTable(
                name: "dispositions");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_LastDispositionId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_TenantId_Outcome_RecycleAt",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "InProspecting",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "LastDispositionId",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "LockedByUserId",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "RecycleAt",
                table: "opportunities");
        }
    }
}
