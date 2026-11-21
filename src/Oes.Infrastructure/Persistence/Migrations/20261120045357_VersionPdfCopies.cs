using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionPdfCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "PdfDocumentId",
                table: "estimate_versions",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SignedPdfDocumentId",
                table: "estimate_versions",
                type: "binary(16)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfDocumentId",
                table: "estimate_versions");

            migrationBuilder.DropColumn(
                name: "SignedPdfDocumentId",
                table: "estimate_versions");
        }
    }
}
