using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_consents",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Purpose = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Evidence = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_consents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_messages",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    FromAddress = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "mediumtext", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SentByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    TemplateId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_messages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Subject = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_templates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sms_messages",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    FromNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ToNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false),
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ProviderSid = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    SentByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    TemplateId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_messages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "suppressions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Address = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    Scope = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppressions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant_phone_numbers",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_phone_numbers", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_recipients",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EmailMessageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false),
                    Address = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_recipients_email_messages_EmailMessageId",
                        column: x => x.EmailMessageId,
                        principalTable: "email_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_contact_consents_TenantId_ContactId_Channel_Purpose_At",
                table: "contact_consents",
                columns: new[] { "TenantId", "ContactId", "Channel", "Purpose", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_email_messages_TenantId_SentAt",
                table: "email_messages",
                columns: new[] { "TenantId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_recipients_EmailMessageId",
                table: "email_recipients",
                column: "EmailMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_email_recipients_TenantId_Address",
                table: "email_recipients",
                columns: new[] { "TenantId", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_email_recipients_TenantId_ContactId",
                table: "email_recipients",
                columns: new[] { "TenantId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_TenantId_Channel_Name",
                table: "message_templates",
                columns: new[] { "TenantId", "Channel", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_TenantId_ContactId_At",
                table: "sms_messages",
                columns: new[] { "TenantId", "ContactId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_TenantId_FromNumber_At",
                table: "sms_messages",
                columns: new[] { "TenantId", "FromNumber", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_TenantId_Channel_Address",
                table: "suppressions",
                columns: new[] { "TenantId", "Channel", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_phone_numbers_Number",
                table: "tenant_phone_numbers",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_consents");

            migrationBuilder.DropTable(
                name: "email_recipients");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "sms_messages");

            migrationBuilder.DropTable(
                name: "suppressions");

            migrationBuilder.DropTable(
                name: "tenant_phone_numbers");

            migrationBuilder.DropTable(
                name: "email_messages");
        }
    }
}
