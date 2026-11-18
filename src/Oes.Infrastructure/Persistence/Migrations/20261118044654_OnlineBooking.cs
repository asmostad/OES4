using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlineBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "availability_rules",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<int>(type: "int", nullable: false),
                    EndTime = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_rules", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "booking_types",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    SlotIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    BufferBeforeMinutes = table.Column<int>(type: "int", nullable: false),
                    BufferAfterMinutes = table.Column<int>(type: "int", nullable: false),
                    MinNoticeMinutes = table.Column<int>(type: "int", nullable: false),
                    DaysAhead = table.Column<int>(type: "int", nullable: false),
                    DailyCapPerHost = table.Column<int>(type: "int", nullable: true),
                    AppointmentTypeKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    AsksAddress = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatesOpportunity = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_types", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    BookingTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    HostUserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    AppointmentId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ReviewStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Line1 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    PostalCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    TextConsent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ReviewedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SourceIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SlotKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "booking_type_hosts",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    BookingTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_type_hosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_booking_type_hosts_booking_types_BookingTypeId",
                        column: x => x.BookingTypeId,
                        principalTable: "booking_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_TenantId_UserId_DayOfWeek",
                table: "availability_rules",
                columns: new[] { "TenantId", "UserId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_booking_type_hosts_BookingTypeId_UserId",
                table: "booking_type_hosts",
                columns: new[] { "BookingTypeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_types_TenantId_Slug",
                table: "booking_types",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_SlotKey",
                table: "bookings",
                column: "SlotKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_TenantId_HostUserId_StartsAt",
                table: "bookings",
                columns: new[] { "TenantId", "HostUserId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_TenantId_ReviewStatus_StartsAt",
                table: "bookings",
                columns: new[] { "TenantId", "ReviewStatus", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_rules");

            migrationBuilder.DropTable(
                name: "booking_type_hosts");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "booking_types");
        }
    }
}
