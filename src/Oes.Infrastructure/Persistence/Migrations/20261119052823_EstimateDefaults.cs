using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstimateDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTaxRate",
                table: "tenants",
                type: "decimal(6,3)",
                precision: 6,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "EstimateTerms",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimateValidDays",
                table: "tenants",
                type: "int",
                nullable: false,
                defaultValue: 30); // what every estimate used before

            migrationBuilder.AddColumn<string>(
                name: "InsuranceEstimateTerms",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceOverheadPct",
                table: "tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceProfitPct",
                table: "tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTaxRate",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EstimateTerms",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EstimateValidDays",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "InsuranceEstimateTerms",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "InsuranceOverheadPct",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "InsuranceProfitPct",
                table: "tenants");
        }
    }
}
