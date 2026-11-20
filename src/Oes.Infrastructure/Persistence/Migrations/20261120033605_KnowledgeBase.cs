using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_articles",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Body = table.Column<string>(type: "mediumtext", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedByUserId = table.Column<byte[]>(type: "binary(16)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_articles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    ArticleId = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<byte[]>(type: "binary(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "knowledge_articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_articles_TenantId_Category_Title",
                table: "knowledge_articles",
                columns: new[] { "TenantId", "Category", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_ArticleId",
                table: "knowledge_chunks",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_chunks_TenantId_ArticleId_Seq",
                table: "knowledge_chunks",
                columns: new[] { "TenantId", "ArticleId", "Seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_articles");
        }
    }
}
