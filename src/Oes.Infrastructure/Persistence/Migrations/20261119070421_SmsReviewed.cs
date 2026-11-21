using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SmsReviewed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "sms_messages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_TenantId_FromNumber_ContactId",
                table: "sms_messages",
                columns: new[] { "TenantId", "FromNumber", "ContactId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sms_messages_TenantId_FromNumber_ContactId",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "sms_messages");
        }
    }
}
