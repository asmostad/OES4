using Oes.Domain.Estimating;
using Oes.Infrastructure.Estimating;

namespace Oes.UnitTests;

public class EstimateTotalsTests
{
    private static EstimateLine L(decimal qty, decimal price, bool material) => new() { Qty = qty, UnitPrice = price, IsMaterial = material };

    [Fact]
    public void Overhead_profit_and_material_tax()
    {
        var lines = new[] { L(25.5m, 285.00m, true), L(1, 450m, false), L(120, 2.35m, true) };
        // 7267.50 + 450 + 282 = 7999.50
        var t = EstimateService.Totals(lines, 10m, 10m, 7.25m, TaxBases.Materials);
        Assert.Equal(7999.50m, t.Subtotal);
        Assert.Equal(799.95m, t.Overhead);
        Assert.Equal(799.95m, t.Profit);
        Assert.Equal(547.34m, t.Tax); // materials only: (7267.50 + 282.00) * 7.25% = 547.33875
        Assert.Equal(t.Subtotal + t.Overhead + t.Profit + t.Tax, t.Total);
    }

    [Fact]
    public void Line_amounts_round_half_away_from_zero() =>
        Assert.Equal(0.13m, L(1, 0.125m, false).Amount);

    [Fact]
    public void Tax_basis_none_and_all()
    {
        var lines = new[] { L(1, 100m, true), L(1, 100m, false) };
        Assert.Equal(0m, EstimateService.Totals(lines, 0, 0, 8m, TaxBases.None).Tax);
        Assert.Equal(16m, EstimateService.Totals(lines, 0, 0, 8m, TaxBases.All).Tax);
        Assert.Equal(8m, EstimateService.Totals(lines, 0, 0, 8m, TaxBases.Materials).Tax);
    }
}

public class CsvTests
{
    [Fact]
    public void Reads_quotes_commas_and_newlines()
    {
        var text = "code,description,price\r\nRFG300,\"Shingle, laminated\",285.00\nDRIP,\"Drip edge \"\"aluminum\"\"\",2.35\n\n\"MULTI\",\"Line one\nline two\",1";
        var rows = Csv.Read(new StringReader(text)).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal("Shingle, laminated", rows[1][1]);
        Assert.Equal("Drip edge \"aluminum\"", rows[2][1]);
        Assert.Equal("Line one\nline two", rows[3][1]);
    }
}
