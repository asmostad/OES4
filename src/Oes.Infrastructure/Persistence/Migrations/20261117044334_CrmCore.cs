using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EventType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    ActorUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "json", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_events", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    OrganizationName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    SourceKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    CautionNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CategoryKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    UploadedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "lookup_values",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSystem = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lookup_values", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AuthorUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    IsPinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "opportunity_stage_changes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    OpportunityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    FromStageId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ToStageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunity_stage_changes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StaleAfterHours = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Line1 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Line2 = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Country = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false),
                    NormalizedKey = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_properties", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "record_links",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ItemId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_record_links", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contact_methods",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    NormalizedValue = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    Label = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_methods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_methods_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contact_relationships",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PersonContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    OrganizationContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Title = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_relationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_relationships_contacts_OrganizationContactId",
                        column: x => x.OrganizationContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contact_relationships_contacts_PersonContactId",
                        column: x => x.PersonContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    PrimaryContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    OwnerUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    SourceKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    StageId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    LostReasonKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    ExpectedValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    IsInsurance = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PerilKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    DateOfLoss = table.Column<DateOnly>(type: "date", nullable: true),
                    StormEventId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ContactAttempts = table.Column<int>(type: "int", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StageChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    WonAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_opportunities_contacts_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunities_pipeline_stages_StageId",
                        column: x => x.StageId,
                        principalTable: "pipeline_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opportunities_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "property_contacts",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ContactId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    RoleKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_property_contacts_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_property_contacts_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "property_structures",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_structures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_property_structures_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_contact_methods_ContactId",
                table: "contact_methods",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_methods_TenantId_NormalizedValue",
                table: "contact_methods",
                columns: new[] { "TenantId", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_relationships_OrganizationContactId",
                table: "contact_relationships",
                column: "OrganizationContactId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_relationships_PersonContactId",
                table: "contact_relationships",
                column: "PersonContactId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_relationships_TenantId_OrganizationContactId",
                table: "contact_relationships",
                columns: new[] { "TenantId", "OrganizationContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_relationships_TenantId_PersonContactId",
                table: "contact_relationships",
                columns: new[] { "TenantId", "PersonContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_TenantId_DisplayName",
                table: "contacts",
                columns: new[] { "TenantId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_TenantId_LastName_FirstName",
                table: "contacts",
                columns: new[] { "TenantId", "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_TenantId_OwnerUserId",
                table: "contacts",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_lookup_values_TenantId_Type_Key",
                table: "lookup_values",
                columns: new[] { "TenantId", "Type", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PrimaryContactId",
                table: "opportunities",
                column: "PrimaryContactId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PropertyId",
                table: "opportunities",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_StageId",
                table: "opportunities",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_Number",
                table: "opportunities",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_OwnerUserId",
                table: "opportunities",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_PrimaryContactId",
                table: "opportunities",
                columns: new[] { "TenantId", "PrimaryContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_PropertyId",
                table: "opportunities",
                columns: new[] { "TenantId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_StageId",
                table: "opportunities",
                columns: new[] { "TenantId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunity_stage_changes_TenantId_OpportunityId",
                table: "opportunity_stage_changes",
                columns: new[] { "TenantId", "OpportunityId" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_Sort",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_properties_TenantId_NormalizedKey",
                table: "properties",
                columns: new[] { "TenantId", "NormalizedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_properties_TenantId_PostalCode",
                table: "properties",
                columns: new[] { "TenantId", "PostalCode" });

            migrationBuilder.CreateIndex(
                name: "IX_property_contacts_ContactId",
                table: "property_contacts",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_property_contacts_PropertyId",
                table: "property_contacts",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_property_contacts_TenantId_ContactId",
                table: "property_contacts",
                columns: new[] { "TenantId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_property_contacts_TenantId_PropertyId",
                table: "property_contacts",
                columns: new[] { "TenantId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_property_structures_PropertyId",
                table: "property_structures",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_record_links_TenantId_EntityType_EntityId_ItemType",
                table: "record_links",
                columns: new[] { "TenantId", "EntityType", "EntityId", "ItemType" });

            migrationBuilder.CreateIndex(
                name: "IX_record_links_TenantId_ItemType_ItemId_EntityType_EntityId",
                table: "record_links",
                columns: new[] { "TenantId", "ItemType", "ItemId", "EntityType", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropTable(
                name: "contact_methods");

            migrationBuilder.DropTable(
                name: "contact_relationships");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "lookup_values");

            migrationBuilder.DropTable(
                name: "notes");

            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropTable(
                name: "opportunity_stage_changes");

            migrationBuilder.DropTable(
                name: "property_contacts");

            migrationBuilder.DropTable(
                name: "property_structures");

            migrationBuilder.DropTable(
                name: "record_links");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "properties");
        }
    }
}
