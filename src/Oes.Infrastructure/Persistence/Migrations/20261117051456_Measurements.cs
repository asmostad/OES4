using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Measurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_types",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    MeasurementMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Color = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Icon = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_types", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "measurement_field_defs",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Group = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurement_field_defs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_measurement_field_defs_trade_types_TradeTypeId",
                        column: x => x.TradeTypeId,
                        principalTable: "trade_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "measurement_sets",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    PropertyId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    StructureId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    TradeTypeId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Source = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    SourceDocumentId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    MeasuredOn = table.Column<DateTime>(type: "date", nullable: true),
                    MeasuredByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    SupersedesId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurement_sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_measurement_sets_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_measurement_sets_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_measurement_sets_property_structures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "property_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_measurement_sets_trade_types_TradeTypeId",
                        column: x => x.TradeTypeId,
                        principalTable: "trade_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "measurement_values",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SetId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    SurfaceKey = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    FieldKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurement_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_measurement_values_measurement_sets_SetId",
                        column: x => x.SetId,
                        principalTable: "measurement_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_field_defs_TenantId_TradeTypeId_Key",
                table: "measurement_field_defs",
                columns: new[] { "TenantId", "TradeTypeId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_measurement_field_defs_TradeTypeId",
                table: "measurement_field_defs",
                column: "TradeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_PropertyId",
                table: "measurement_sets",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_SourceDocumentId",
                table: "measurement_sets",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_StructureId",
                table: "measurement_sets",
                column: "StructureId");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_TenantId_PropertyId",
                table: "measurement_sets",
                columns: new[] { "TenantId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_TenantId_StructureId_TradeTypeId_VersionNo",
                table: "measurement_sets",
                columns: new[] { "TenantId", "StructureId", "TradeTypeId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_measurement_sets_TradeTypeId",
                table: "measurement_sets",
                column: "TradeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_measurement_values_SetId_SurfaceKey_FieldKey",
                table: "measurement_values",
                columns: new[] { "SetId", "SurfaceKey", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_types_TenantId_Code",
                table: "trade_types",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "measurement_field_defs");

            migrationBuilder.DropTable(
                name: "measurement_values");

            migrationBuilder.DropTable(
                name: "measurement_sets");

            migrationBuilder.DropTable(
                name: "trade_types");
        }
    }
}
