using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Insurance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carriers",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    ClaimsEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true),
                    ClaimsFax = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    Website = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    PreferredSubmission = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carriers", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "storm_events",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    EventDate = table.Column<DateTime>(type: "date", nullable: false),
                    PerilKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    MaxHailSizeIn = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    MaxWindMph = table.Column<int>(type: "int", nullable: true),
                    ZipCodes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storm_events", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant_carriers",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CarrierId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    ClaimsEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true),
                    ClaimsFax = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    Website = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    PreferredSubmission = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsHidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_carriers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_carriers_carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "insurance_claims",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    TenantCarrierId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    StormEventId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ClaimNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    PolicyNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    DateOfLoss = table.Column<DateTime>(type: "date", nullable: true),
                    PerilKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Deductible = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AdjusterContactId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    DeskAdjusterContactId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FiledOn = table.Column<DateTime>(type: "date", nullable: true),
                    InspectionOn = table.Column<DateTime>(type: "date", nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insurance_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_insurance_claims_contacts_AdjusterContactId",
                        column: x => x.AdjusterContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_insurance_claims_contacts_DeskAdjusterContactId",
                        column: x => x.DeskAdjusterContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_insurance_claims_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_insurance_claims_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_insurance_claims_storm_events_StormEventId",
                        column: x => x.StormEventId,
                        principalTable: "storm_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_insurance_claims_tenant_carriers_TenantCarrierId",
                        column: x => x.TenantCarrierId,
                        principalTable: "tenant_carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "supplements",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ClaimId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SentOn = table.Column<DateTime>(type: "date", nullable: true),
                    RespondedOn = table.Column<DateTime>(type: "date", nullable: true),
                    SubmittedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplements_insurance_claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "insurance_claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "carriers",
                columns: new[] { "Id", "Address", "ClaimsEmail", "ClaimsFax", "CreatedAt", "IsActive", "Name", "Phone", "PreferredSubmission", "UpdatedAt", "Website" },
                values: new object[,]
                {
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 1 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Allstate", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 37 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Universal Property", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 36 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Travelers", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 35 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "The Hartford", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 34 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "State Farm", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 33 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Shelter Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 32 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Safeco", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 25 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Progressive", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 24 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Nationwide", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 23 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Liberty Mutual", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 22 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Lemonade", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 21 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Kemper", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 38 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "USAA", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 20 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Homesite", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 18 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Grange Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 17 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Foremost Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 16 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Farmers Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 9 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Erie Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 8 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Encompass Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 7 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "COUNTRY Financial", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 6 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Citizens Property Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 5 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Cincinnati Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 4 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Chubb", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 3 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Auto-Owners Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 2 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "American Family Insurance", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 19 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Hippo", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new byte[] { 0, 0, 0, 0, 0, 0, 112, 0, 128, 0, 0, 0, 0, 0, 0, 39 }, null, null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Westfield", null, null, new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_StormEventId",
                table: "opportunities",
                column: "StormEventId");

            migrationBuilder.CreateIndex(
                name: "IX_carriers_Name",
                table: "carriers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_AdjusterContactId",
                table: "insurance_claims",
                column: "AdjusterContactId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_DeskAdjusterContactId",
                table: "insurance_claims",
                column: "DeskAdjusterContactId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_OpportunityId",
                table: "insurance_claims",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_PropertyId",
                table: "insurance_claims",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_StormEventId",
                table: "insurance_claims",
                column: "StormEventId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_TenantCarrierId",
                table: "insurance_claims",
                column: "TenantCarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_TenantId_ClaimNumber",
                table: "insurance_claims",
                columns: new[] { "TenantId", "ClaimNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_TenantId_OpportunityId",
                table: "insurance_claims",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_TenantId_PropertyId",
                table: "insurance_claims",
                columns: new[] { "TenantId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_claims_TenantId_StormEventId",
                table: "insurance_claims",
                columns: new[] { "TenantId", "StormEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_storm_events_TenantId_EventDate",
                table: "storm_events",
                columns: new[] { "TenantId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_supplements_ClaimId",
                table: "supplements",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_supplements_TenantId_ClaimId",
                table: "supplements",
                columns: new[] { "TenantId", "ClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_carriers_CarrierId",
                table: "tenant_carriers",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_carriers_TenantId_CarrierId",
                table: "tenant_carriers",
                columns: new[] { "TenantId", "CarrierId" });

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_storm_events_StormEventId",
                table: "opportunities",
                column: "StormEventId",
                principalTable: "storm_events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_storm_events_StormEventId",
                table: "opportunities");

            migrationBuilder.DropTable(
                name: "supplements");

            migrationBuilder.DropTable(
                name: "insurance_claims");

            migrationBuilder.DropTable(
                name: "storm_events");

            migrationBuilder.DropTable(
                name: "tenant_carriers");

            migrationBuilder.DropTable(
                name: "carriers");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_StormEventId",
                table: "opportunities");
        }
    }
}
