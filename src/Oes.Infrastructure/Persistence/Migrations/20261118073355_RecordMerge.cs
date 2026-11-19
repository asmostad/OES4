using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "MergedIntoId",
                table: "properties",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MergedIntoId",
                table: "contacts",
                type: "binary(16)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "contacts");
        }
    }
}
