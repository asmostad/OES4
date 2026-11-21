using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertDedupeAndBookingReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupeKey",
                table: "notifications",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "bookings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_UserId_DedupeKey",
                table: "notifications",
                columns: new[] { "TenantId", "UserId", "DedupeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_UserId_DedupeKey",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "DedupeKey",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "bookings");
        }
    }
}
