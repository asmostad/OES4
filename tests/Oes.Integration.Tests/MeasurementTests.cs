using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class MeasurementTests(MySqlFixture fx)
{
    private static async Task<(Guid Property, Guid Structure, TradeType Roofing, TradeType Siding)> SetupAsync(AsyncServiceScope s, string street)
    {
        var props = s.Get<PropertyService>();
        var id = await props.CreateAsync(new PropertyInput { Line1 = street, City = "Lincoln", State = "NE", PostalCode = "68502" });
        var structure = (await props.GetAsync(id)).Structures.Single();
        var trades = await s.Get<MeasurementService>().TradesAsync();
        return (id, structure.Id, trades.Single(t => t.Code == "roofing"), trades.Single(t => t.Code == "siding"));
    }

    private static MeasurementInput Roof(decimal area, decimal? valleys = null) => new()
    {
        Source = "eagleview",
        Values = new() { ["|total_area"] = area, ["|predominant_pitch"] = 6, ["|valleys"] = valleys },
    };

    [Fact]
    public async Task New_tenants_get_default_trades_and_fields()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var trades = await s.Get<MeasurementService>().TradesAsync();
        Assert.Equal(MeasurementDefaults.Trades.Length, trades.Count);
        var roofing = trades.Single(x => x.Code == "roofing");
        Assert.Equal(MeasurementModes.Roof, roofing.MeasurementMode);
        Assert.Contains(await s.Get<MeasurementService>().FieldsAsync(roofing.Id), f => f.Key == "valleys" && f.Unit == "LF");
    }

    [Fact]
    public async Task New_numbers_make_a_new_version_and_keep_the_old_one()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var (property, structure, roofing, _) = await SetupAsync(s, "10 Version Ave");
        var svc = s.Get<MeasurementService>();

        var v1 = await svc.SaveVersionAsync(property, structure, roofing.Id, Roof(2400, 30));
        var v2 = await svc.SaveVersionAsync(property, structure, roofing.Id, Roof(2650, 42),
            (new MemoryStream("%PDF-1.4 report"u8.ToArray()), "eagleview.pdf", "application/pdf"));

        var d1 = await svc.GetSetAsync(v1);
        var d2 = await svc.GetSetAsync(v2);
        Assert.False(d1.Set.IsCurrent);
        Assert.True(d2.Set.IsCurrent);
        Assert.Equal(2, d2.Set.VersionNo);
        Assert.Equal(v1, d2.Set.SupersedesId);
        Assert.Equal(2400m, d1.Value("", "total_area")); // old version untouched
        Assert.Equal(2400m, d2.PreviousValue("", "total_area"));
        Assert.NotNull(d2.SourceDocument);
        Assert.Equal("measurement_report", d2.SourceDocument!.CategoryKey);
        Assert.Equal("26.5 sq · 6/12 · 42 LF valleys", MeasurementService.Headline(MeasurementModes.Roof, d2.Set.Values));

        var overview = await svc.ForPropertyAsync(property);
        var current = Assert.Single(overview.Current);
        Assert.Equal(v2, current.Id);
        Assert.Equal(2, current.VersionCount);

        // Reverting makes v1 current without deleting v2.
        await svc.MakeCurrentAsync(v1);
        Assert.True((await svc.GetSetAsync(v1)).Set.IsCurrent);
        Assert.False((await svc.GetSetAsync(v2)).Set.IsCurrent);
        Assert.Equal(2, await s.Get<OesDbContext>().MeasurementSets.CountAsync(x => x.StructureId == structure));
    }

    [Fact]
    public async Task Elevation_trades_store_values_per_wall_side()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var (property, structure, _, siding) = await SetupAsync(s, "20 Elevation Rd");
        var svc = s.Get<MeasurementService>();
        var id = await svc.SaveVersionAsync(property, structure, siding.Id, new MeasurementInput
        {
            Values = new() { ["front|wall_area"] = 600, ["rear|wall_area"] = 550, ["left|wall_area"] = 300, ["right|wall_area"] = 300, ["front|openings_area"] = 120 },
        });
        var d = await svc.GetSetAsync(id);
        Assert.Equal(1750m, d.Total("wall_area"));
        Assert.Equal("1,750 sq ft walls · 1,630 net", MeasurementService.Headline(MeasurementModes.Elevation, d.Set.Values));
    }

    [Fact]
    public async Task Bad_input_is_rejected()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var (property, structure, roofing, siding) = await SetupAsync(s, "30 Reject Blvd");
        var svc = s.Get<MeasurementService>();

        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, roofing.Id, new MeasurementInput()));
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, roofing.Id, new MeasurementInput { Values = new() { ["|total_area"] = -5 } }));
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, roofing.Id, new MeasurementInput { Values = new() { ["|not_a_field"] = 5 } }));
        // Roofing has no per-side surfaces; siding has no whole-structure ones.
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, roofing.Id, new MeasurementInput { Values = new() { ["front|total_area"] = 5 } }));
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, siding.Id, new MeasurementInput { Values = new() { ["|wall_area"] = 5 } }));

        // A structure from another property is refused.
        var (other, otherStructure, _, _) = await SetupAsync(s, "31 Other Blvd");
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, otherStructure, roofing.Id, Roof(1000)));
    }

    [Fact]
    public async Task Measurements_follow_property_visibility()
    {
        var (t, _) = await fx.CreateTenantAsync();
        var repA = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var repB = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid setId, propertyId;
        await using (var s = fx.ScopeAs(t.Id, repA))
        {
            var (property, structure, roofing, _) = await SetupAsync(s, "40 Private Pl");
            propertyId = property;
            setId = await s.Get<MeasurementService>().SaveVersionAsync(property, structure, roofing.Id, Roof(1800));
        }
        await using (var s = fx.ScopeAs(t.Id, repB))
        {
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<MeasurementService>().GetSetAsync(setId));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<MeasurementService>().ForPropertyAsync(propertyId));
            Assert.DoesNotContain(await s.Get<MeasurementService>().RecentAsync(), r => r.Set.Id == setId);
        }
    }

    [Fact]
    public async Task Retired_fields_keep_their_history_and_custom_fields_work()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var (property, structure, roofing, _) = await SetupAsync(s, "50 Custom Ct");
        var svc = s.Get<MeasurementService>();
        var v1 = await svc.SaveVersionAsync(property, structure, roofing.Id, Roof(2000, 25));

        var valleys = (await svc.FieldsAsync(roofing.Id)).Single(f => f.Key == "valleys");
        await svc.SaveFieldAsync(roofing.Id, valleys.Id, valleys.Label, valleys.Unit, valleys.Group, isActive: false);
        await svc.SaveFieldAsync(roofing.Id, null, "Satellite dishes", "ea", "Penetrations", true);

        Assert.DoesNotContain(await svc.FieldsAsync(roofing.Id), f => f.Key == "valleys");
        Assert.Contains((await svc.GetSetAsync(v1)).Fields, f => f.Key == "valleys"); // still shown on the old version
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveVersionAsync(property, structure, roofing.Id, Roof(2000, 25)));
        var v2 = await svc.SaveVersionAsync(property, structure, roofing.Id, new MeasurementInput { Values = new() { ["|total_area"] = 2000, ["|satellite_dishes"] = 1 } });
        Assert.Equal(1m, (await svc.GetSetAsync(v2)).Value("", "satellite_dishes"));

        // Mode can't change once a trade has measurements.
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.SaveTradeAsync(roofing.Id, roofing.Name, MeasurementModes.Linear, roofing.Color, true));
    }
}
