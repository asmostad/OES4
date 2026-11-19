using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusyCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "busy_calendars",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UrlProtected = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    Host = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ShowOnCalendar = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    BlockCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_busy_calendars", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "busy_blocks",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    BusyCalendarId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_busy_blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_busy_blocks_busy_calendars_BusyCalendarId",
                        column: x => x.BusyCalendarId,
                        principalTable: "busy_calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_busy_blocks_BusyCalendarId",
                table: "busy_blocks",
                column: "BusyCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_busy_blocks_TenantId_BusyCalendarId",
                table: "busy_blocks",
                columns: new[] { "TenantId", "BusyCalendarId" });

            migrationBuilder.CreateIndex(
                name: "IX_busy_blocks_TenantId_UserId_StartsAt",
                table: "busy_blocks",
                columns: new[] { "TenantId", "UserId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_busy_calendars_TenantId_UserId",
                table: "busy_calendars",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "busy_blocks");

            migrationBuilder.DropTable(
                name: "busy_calendars");
        }
    }
}
