using Oes.Domain.Estimating;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Estimating;

namespace Oes.UnitTests;

public class TakeoffEngineTests
{
    private static readonly MeasurementFieldDef[] RoofFields =
    [
        new() { Key = "total_area", Unit = "sq ft", Label = "Total roof area" }, new() { Key = "ridges", Unit = "LF" }, new() { Key = "hips", Unit = "LF" },
        new() { Key = "eaves", Unit = "LF" }, new() { Key = "rakes", Unit = "LF" }, new() { Key = "layers", Unit = "ea" }, new() { Key = "suggested_waste", Unit = "%" },
    ];

    private static Dictionary<string, decimal> Drivers(decimal area = 2805, decimal ridges = 40, decimal hips = 60, decimal layers = 1, decimal waste = 0) =>
        TakeoffEngine.Values(new Dictionary<string, decimal>
        {
            ["total_area"] = area, ["ridges"] = ridges, ["hips"] = hips, ["eaves"] = 120, ["rakes"] = 80, ["layers"] = layers, ["suggested_waste"] = waste,
        }, RoofFields);

    private static readonly Product Shingle = new() { Name = "Arch shingle", Unit = "SQ", DefaultPrice = 130m };
    private static readonly Product Ridge = new() { Name = "Ridge cap", Unit = "BD", DefaultPrice = 60m };

    private static TakeoffChoices Pick(params (string Slot, Product P)[] picks) =>
        new(new HashSet<string>(), picks.ToDictionary(p => p.Slot, p => p.P));

    [Fact]
    public void Derived_drivers_cover_squares_and_combined_edges()
    {
        var d = Drivers();
        Assert.Equal(28.05m, d["total_area_squares"]);
        Assert.Equal(100m, d["ridge_hip"]);
        Assert.Equal(200m, d["eave_rake"]);
        Assert.Equal(1m, d[TakeoffEngine.Flat]);
        Assert.Contains(TakeoffEngine.Drivers(new TradeType { MeasurementMode = MeasurementModes.Roof }, RoofFields), x => x.Key == "eave_rake");
    }

    [Theory]
    [InlineData(30.855, TakeoffRounding.Up, 0.33, 30.03 + 0.99)]   // 93.5 thirds -> 94 thirds
    [InlineData(10.2, TakeoffRounding.Up, 1, 11)]
    [InlineData(10.2, TakeoffRounding.Down, 1, 10)]
    [InlineData(10.5, TakeoffRounding.Nearest, 1, 11)]
    [InlineData(24, TakeoffRounding.Up, 10, 30)]
    [InlineData(3, TakeoffRounding.Up, 1, 3)]
    [InlineData(10.2345, TakeoffRounding.None, 1, 10.235)]
    public void Rounding_goes_to_a_multiple(double qty, string mode, double to, double expected) =>
        Assert.Equal((decimal)expected, TakeoffEngine.Round((decimal)qty, mode, (decimal)to));

    [Fact]
    public void Quantity_is_driver_times_multiplier_plus_waste_then_rounded_and_floored()
    {
        var shingles = new TakeoffRule { Name = "Shingles", Slot = "shingle", Driver = "total_area_squares", Multiplier = 1, WastePercent = 10, RoundTo = 0.33m };
        var ridge = new TakeoffRule { Name = "Ridge", Slot = "ridge_cap", Driver = "ridge_hip", Multiplier = 0.05m, WastePercent = 5, MinQty = 1, Sort = 2 };
        var lines = TakeoffEngine.Run([shingles, ridge], Drivers(), new Dictionary<Guid, Product>(), Pick(("shingle", Shingle), ("ridge_cap", Ridge)));
        Assert.Equal(30.855m, lines[0].RawQty);                    // 28.05 SQ + 10%
        Assert.Equal(31.02m, lines[0].Qty);                        // up to 94 thirds
        Assert.Equal(6m, lines[1].Qty);                            // 100 LF × 0.05 × 1.05 = 5.25 -> 6

        // The measured waste wins when the rule says so; the floor applies to small jobs.
        shingles.UseSuggestedWaste = true;
        Assert.Equal(32.2575m, TakeoffEngine.Run([shingles], Drivers(waste: 15), new Dictionary<Guid, Product>(), Pick(("shingle", Shingle)))[0].RawQty);
        Assert.Equal(1m, TakeoffEngine.Run([ridge], Drivers(ridges: 4, hips: 0), new Dictionary<Guid, Product>(), Pick(("ridge_cap", Ridge)))[0].Qty);
    }

    [Fact]
    public void Product_coverage_replaces_the_rule_multiplier()
    {
        var bigBundle = new Product { Name = "Ridge cap 33 LF", Unit = "BD", TakeoffMultiplier = 1m / 33 };
        var ridge = new TakeoffRule { Name = "Ridge", Slot = "ridge_cap", Driver = "ridge_hip", Multiplier = 0.05m };
        Assert.Equal(4m, TakeoffEngine.Run([ridge], Drivers(), new Dictionary<Guid, Product>(), Pick(("ridge_cap", bigBundle)))[0].Qty); // 100/33 -> 4
    }

    [Fact]
    public void Conditions_options_and_missing_products()
    {
        var secondLayer = new TakeoffRule { Name = "Extra layer", ProductId = Guid.NewGuid(), Driver = "total_area_squares", Cond1Field = "layers", Cond1Op = "gte", Cond1Value = 2 };
        var iceWater = new TakeoffRule { Name = "Ice & water", Slot = "ice_water", Driver = "eaves", Multiplier = 0.005m, OptionKey = "ice_water" };
        var nothing = new TakeoffRule { Name = "Valley metal", Slot = "valley", Driver = "valleys", Multiplier = 0.1m };
        var inactive = new TakeoffRule { Name = "Off", ProductId = Guid.NewGuid(), Driver = TakeoffEngine.Flat, IsActive = false };
        var fixedProduct = new Product { Name = "Tear-off", Unit = "SQ" };
        var products = new Dictionary<Guid, Product> { [secondLayer.ProductId!.Value] = fixedProduct };

        Assert.Empty(TakeoffEngine.Run([secondLayer, iceWater, nothing, inactive], Drivers(), products, Pick())); // 1 layer, option off, no valleys
        var two = TakeoffEngine.Run([secondLayer, iceWater], Drivers(layers: 2), products,
            new TakeoffChoices(new HashSet<string> { "ice_water" }, new Dictionary<string, Product>()));
        Assert.Equal(("Tear-off", (string?)null), (two[0].Description, two[0].Problem));
        Assert.Contains("Ice water", two[1].Problem);           // option on, but no product picked for the slot
        Assert.Equal(1m, two[1].Qty);
    }
}
