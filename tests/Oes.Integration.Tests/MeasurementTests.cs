using System.Net;
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

    [Fact]
    public async Task Field_app_reads_and_takes_measurements_on_site()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid property, structure, roofingId, sidingId, v1;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var (p, st, roofingTrade, sidingTrade) = await SetupAsync(s, "8 Tape Ln");
            (property, structure, roofingId, sidingId) = (p, st, roofingTrade.Id, sidingTrade.Id);
            v1 = await s.Get<MeasurementService>().SaveVersionAsync(property, structure, roofingId, Roof(2400, 30));
        }
        var api = fx.App.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        var tok = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(api, "/api/v1/auth/token", new { email = admin.Email, password = MySqlFixture.Password });
        var access = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(tok.Content)).GetProperty("accessToken").GetString();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        async Task<System.Text.Json.JsonElement> Json(HttpResponseMessage r)
        {
            Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            return await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(r.Content);
        }
        Task<HttpResponseMessage> Post(string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = System.Net.Http.Json.JsonContent.Create(body) };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return api.SendAsync(req);
        }

        // What's on file, with each trade's fields and surfaces.
        var onFile = await Json(await api.GetAsync($"/api/v1/properties/{property}/measurements"));
        Assert.True(onFile.GetProperty("canMeasure").GetBoolean());
        var siding = onFile.GetProperty("trades").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == sidingId);
        Assert.Equal(["front", "rear", "left", "right"], siding.GetProperty("surfaces").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(v1, onFile.GetProperty("current").EnumerateArray().Single().GetProperty("id").GetGuid());

        // A tape measurement on site makes version 2; version 1 stays as history and shows as "previous".
        var saved = await Json(await Post($"/api/v1/properties/{property}/measurements", new
        {
            structureId = structure, tradeTypeId = roofingId, notes = "Measured from the ladder", values = new Dictionary<string, decimal?> { ["|total_area"] = 2450, ["|valleys"] = 32, ["|ridges"] = null },
        }));
        var v2 = saved.GetProperty("setId").GetGuid();
        var set = await Json(await api.GetAsync($"/api/v1/measurement-sets/{v2}"));
        Assert.Equal((2, true, "field_tape"), (set.GetProperty("versionNo").GetInt32(), set.GetProperty("isCurrent").GetBoolean(), set.GetProperty("source").GetString()));
        Assert.Equal(2450m, set.GetProperty("values").GetProperty("|total_area").GetDecimal());
        Assert.Equal(2400m, set.GetProperty("previous").GetProperty("|total_area").GetDecimal());
        Assert.Equal(2, set.GetProperty("versions").GetArrayLength());
        Assert.False((await Json(await api.GetAsync($"/api/v1/measurement-sets/{v1}"))).GetProperty("isCurrent").GetBoolean());

        // Per-side siding, a new structure, and refusals that read as messages.
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/v1/properties/{property}/structures", new { name = "Detached garage" })).StatusCode);
        var garage = (await Json(await api.GetAsync($"/api/v1/properties/{property}/measurements"))).GetProperty("structures").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "Detached garage").GetProperty("id").GetGuid();
        var wall = siding.GetProperty("fields")[0].GetProperty("key").GetString();
        await Json(await Post($"/api/v1/properties/{property}/measurements", new { structureId = garage, tradeTypeId = sidingId, values = new Dictionary<string, decimal?> { [$"front|{wall}"] = 300 } }));
        var empty = await Post($"/api/v1/properties/{property}/measurements", new { structureId = garage, tradeTypeId = roofingId, values = new Dictionary<string, decimal?>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Contains("Enter at least one measurement.", await empty.Content.ReadAsStringAsync());
        var unknown = await Post($"/api/v1/properties/{property}/measurements", new { structureId = garage, tradeTypeId = roofingId, values = new Dictionary<string, decimal?> { ["front|total_area"] = 1 } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
    }
}
