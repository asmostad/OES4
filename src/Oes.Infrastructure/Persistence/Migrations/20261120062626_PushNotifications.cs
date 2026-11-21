using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PushedAt",
                table: "notifications",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_push_tokens",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    UserId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Token = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DisabledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_push_tokens", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PushedAt_CreatedAt",
                table: "notifications",
                columns: new[] { "PushedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_push_tokens_TenantId_Token",
                table: "device_push_tokens",
                columns: new[] { "TenantId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_push_tokens_TenantId_UserId",
                table: "device_push_tokens",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_push_tokens");

            migrationBuilder.DropIndex(
                name: "IX_notifications_PushedAt_CreatedAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "PushedAt",
                table: "notifications");
        }
    }
}
