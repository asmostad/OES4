using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentAttendees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                table: "appointments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "appointment_attendees",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    AppointmentId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_attendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointment_attendees_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_attendees_AppointmentId",
                table: "appointment_attendees",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_attendees_TenantId_AppointmentId_UserId",
                table: "appointment_attendees",
                columns: new[] { "TenantId", "AppointmentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointment_attendees_TenantId_UserId",
                table: "appointment_attendees",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_attendees");

            migrationBuilder.DropColumn(
                name: "IsAllDay",
                table: "appointments");
        }
    }
}
