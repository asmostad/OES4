using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NurtureSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrollment_step_runs",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EnrollmentId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StepId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StepIndex = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollment_step_runs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SequenceId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StepIndex = table.Column<int>(type: "int", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    EnrolledByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    StopReason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sequences",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StopOnReply = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StopWhenClosed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sequence_steps",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SequenceId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    DelayMinutes = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequence_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sequence_steps_sequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "sequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_step_runs_EnrollmentId_CreatedAt",
                table: "enrollment_step_runs",
                columns: new[] { "EnrollmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_Status_NextRunAt",
                table: "enrollments",
                columns: new[] { "Status", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_TenantId_ContactId_SequenceId",
                table: "enrollments",
                columns: new[] { "TenantId", "ContactId", "SequenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_TenantId_OpportunityId",
                table: "enrollments",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_sequence_steps_SequenceId_Sort",
                table: "sequence_steps",
                columns: new[] { "SequenceId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_sequences_TenantId_Name",
                table: "sequences",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollment_step_runs");

            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropTable(
                name: "sequence_steps");

            migrationBuilder.DropTable(
                name: "sequences");
        }
    }
}
