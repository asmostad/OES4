using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Crm;
using Oes.Domain.Insurance;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class InsuranceTests(MySqlFixture fx)
{
    private static readonly Guid StateFarm = Guid.Parse("00000000-0000-7000-8000-000000000022");

    private static async Task<Guid> LeadAsync(AsyncServiceScope s, Guid ownerId, string? street = null, string zip = "68502")
    {
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Claim", LastName = "Holder", OwnerUserId = ownerId });
        Guid? property = street is null ? null
            : await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = street, City = "Lincoln", State = "NE", PostalCode = zip });
        return await s.Get<OpportunityService>().CreateAsync(new OpportunityInput
        {
            PrimaryContactId = contact, PropertyId = property, OwnerUserId = ownerId, IsInsurance = true, PerilKey = "hail",
            DateOfLoss = new DateOnly(2026, 5, 14),
        });
    }

    [Fact]
    public async Task Platform_carriers_are_seeded_and_tenant_edits_stay_private()
    {
        var (a, adminA) = await fx.CreateTenantAsync();
        var (b, adminB) = await fx.CreateTenantAsync();

        await using (var s = fx.ScopeAs(a.Id, adminA))
        {
            var list = await s.Get<CarrierService>().ListAsync();
            Assert.Contains(list, c => c.Name == "State Farm" && !c.IsCustom);
            await s.Get<CarrierService>().SaveOverridesAsync(StateFarm, new CarrierInput { Name = "State Farm", ClaimsEmail = "claims-a@example.com", Notes = "A's notes" });
            await s.Get<CarrierService>().SaveCustomAsync(null, new CarrierInput { Name = "Tiny Mutual of Nebraska" });
            var after = await s.Get<CarrierService>().ListAsync();
            var sf = Assert.Single(after, c => c.Name == "State Farm");
            Assert.Equal("claims-a@example.com", sf.ClaimsEmail);
            Assert.True(sf.HasOverrides);
            Assert.Contains(after, c => c.Name == "Tiny Mutual of Nebraska" && c.IsCustom);
        }

        await using (var s = fx.ScopeAs(b.Id, adminB))
        {
            var list = await s.Get<CarrierService>().ListAsync();
            var sf = Assert.Single(list, c => c.Name == "State Farm");
            Assert.Null(sf.ClaimsEmail);
            Assert.DoesNotContain(list, c => c.Name == "Tiny Mutual of Nebraska");
        }

        // The platform row itself never changes.
        await using (var s = fx.ScopeFor(null))
            Assert.Null((await s.Get<OesDbContext>().Carriers.SingleAsync(c => c.Id == StateFarm)).ClaimsEmail);
    }

    [Fact]
    public async Task Claims_can_start_empty_and_a_claim_number_marks_them_filed()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var opp = await LeadAsync(s, admin.Id);
        var claims = s.Get<ClaimService>();

        var id = await claims.CreateAsync(opp, new ClaimInput());
        var d = await claims.GetAsync(id);
        Assert.Equal(ClaimStatuses.NotFiled, d.Claim.Status);
        Assert.Equal(new DateOnly(2026, 5, 14), d.Claim.DateOfLoss); // copied from the lead
        Assert.Null(d.Claim.ClaimNumber);

        await claims.UpdateAsync(id, new ClaimInput { ClaimNumber = "CLM-1", CarrierChoice = $"p:{StateFarm}", DateOfLoss = d.Claim.DateOfLoss });
        d = await claims.GetAsync(id);
        Assert.Equal(ClaimStatuses.Open, d.Claim.Status);
        Assert.NotNull(d.Claim.FiledOn);
        Assert.Equal("State Farm", d.Carrier!.Name);

        // Picking the same platform carrier again reuses the tenant's row.
        await claims.UpdateAsync(id, new ClaimInput { ClaimNumber = "CLM-1", CarrierChoice = $"p:{StateFarm}", Status = ClaimStatuses.Open });
        Assert.Equal(1, await s.Get<OesDbContext>().TenantCarriers.CountAsync(c => c.CarrierId == StateFarm));
    }

    [Fact]
    public async Task Supplements_add_to_the_approved_total()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var claims = s.Get<ClaimService>();
        var id = await claims.CreateAsync(await LeadAsync(s, admin.Id), new ClaimInput { ClaimNumber = "X", ApprovedAmount = 10000m });
        var supp = await claims.SaveSupplementAsync(id, null, new SupplementInput { Reason = "Drip edge", RequestedAmount = 900m });
        await claims.SaveSupplementAsync(id, supp, new SupplementInput { Reason = "Drip edge", Status = SupplementStatuses.Partial, RequestedAmount = 900m, ApprovedAmount = 600m });

        var d = await claims.GetAsync(id);
        Assert.Equal(10600m, d.TotalApproved);
        var row = Assert.Single(d.Supplements).Supplement;
        Assert.NotNull(row.SentOn);
        Assert.NotNull(row.RespondedOn);
    }

    [Fact]
    public async Task Claims_follow_their_opportunitys_visibility()
    {
        var (t, _) = await fx.CreateTenantAsync();
        var repA = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var repB = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid claimId;
        await using (var s = fx.ScopeAs(t.Id, repA)) claimId = await s.Get<ClaimService>().CreateAsync(await LeadAsync(s, repA.Id), new ClaimInput { ClaimNumber = "PRIVATE-1" });
        await using (var s = fx.ScopeAs(t.Id, repB))
        {
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<ClaimService>().GetAsync(claimId));
            Assert.DoesNotContain(await s.Get<ClaimService>().ListAsync(null, null, null, null), c => c.Id == claimId);
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<ClaimService>().UpdateAsync(claimId, new ClaimInput()));
        }
    }

    [Fact]
    public async Task Claim_notes_roll_up_to_the_property_attached_later()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var opp = await LeadAsync(s, admin.Id);
        var claimId = await s.Get<ClaimService>().CreateAsync(opp, new ClaimInput());
        await s.Get<RecordItems>().AddNoteAsync(EntityTypes.Claim, claimId, "Adjuster approved full roof");

        var propertyId = await s.Get<OpportunityService>().AttachNewPropertyAsync(opp, new PropertyInput { Line1 = "77 Claim Ct", City = "Lincoln", State = "NE", PostalCode = "68502" });

        Assert.Equal(propertyId, (await s.Get<ClaimService>().GetAsync(claimId)).Claim.PropertyId);
        var onProperty = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Property, propertyId, TimelineScope.Everything, ItemTypes.Note);
        Assert.Contains(onProperty, i => i.Text == "Adjuster approved full roof" && i.EntityType == EntityTypes.Claim);
        var onOpp = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Opportunity, opp, TimelineScope.ThisRecord, ItemTypes.Note);
        Assert.DoesNotContain(onOpp, i => i.Text == "Adjuster approved full roof"); // primary link is the claim
    }

    [Fact]
    public async Task Storms_build_a_canvass_list_and_suggest_themselves()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var stormId = await s.Get<StormService>().SaveAsync(null, new StormInput
        {
            Name = "Test hail", EventDate = new DateOnly(2026, 5, 14), MaxHailSizeIn = 1.5m, ZipCodes = "68502;68516 68999-1234",
        });
        var storm = await s.Get<StormService>().GetAsync(stormId);
        Assert.Equal("68502, 68516, 68999", storm.ZipCodes);

        var props = s.Get<PropertyService>();
        var worked = await props.CreateAsync(new PropertyInput { Line1 = "1 Worked St", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var fresh = await props.CreateAsync(new PropertyInput { Line1 = "2 Fresh St", City = "Lincoln", State = "NE", PostalCode = "68516" });
        var outside = await props.CreateAsync(new PropertyInput { Line1 = "3 Far St", City = "Omaha", State = "NE", PostalCode = "68104" });

        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Already", LastName = "Worked", OwnerUserId = admin.Id });
        await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = worked, OwnerUserId = admin.Id, IsInsurance = true, StormEventId = stormId });

        var canvass = (await s.Get<StormService>().UntouchedPropertiesAsync(stormId)).Select(p => p.Id).ToList();
        Assert.Contains(fresh, canvass);
        Assert.DoesNotContain(worked, canvass);
        Assert.DoesNotContain(outside, canvass);

        Assert.Equal(stormId, (await s.Get<StormService>().SuggestAsync("68516", new DateOnly(2026, 5, 15)))?.Id);
        Assert.Null(await s.Get<StormService>().SuggestAsync("68516", new DateOnly(2026, 7, 1)));
        Assert.Equal(1, (await s.Get<StormService>().ListAsync()).Single(x => x.Id == stormId).Opportunities);
    }

    [Fact]
    public async Task Pipeline_stage_rules_protect_won_lost_and_default()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var settings = s.Get<ListSettingsService>();
        var stages = await settings.StagesAsync();
        var won = stages.Single(x => x.Outcome == Outcomes.Won);
        var first = stages.Single(x => x.IsDefault);

        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveStageAsync(new StageInput { Id = won.Id, Name = "Sold", Color = "#10b981", IsActive = false }));
        await settings.SaveStageAsync(new StageInput { Id = won.Id, Name = "Sold", Color = "#10b981", IsActive = true });
        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveStageAsync(new StageInput { Id = first.Id, Name = first.Name, Color = first.Color, IsActive = false }));

        await settings.SaveValueAsync(LookupTypes.LeadSource, null, "Yard Sign", true);
        await settings.SaveValueAsync(LookupTypes.LeadSource, null, "Yard sign!", true);
        var keys = (await settings.ValuesAsync(LookupTypes.LeadSource)).Select(v => v.Key).ToList();
        Assert.Contains("yard_sign", keys);
        Assert.Contains("yard_sign_2", keys);
    }

    [Fact]
    public async Task Field_app_canvasses_a_storm_nearest_door_first_and_takes_leads_at_the_door()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid stormId, near, far, unplaced;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            stormId = await s.Get<StormService>().SaveAsync(null, new StormInput { Name = "Canvass hail", ZipCodes = "68502, 68516" });
            var props = s.Get<PropertyService>();
            far = await props.CreateAsync(new PropertyInput { Line1 = "1 Far Rd", City = "Lincoln", State = "NE", PostalCode = "68516" });
            near = await props.CreateAsync(new PropertyInput { Line1 = "9 Near Rd", City = "Lincoln", State = "NE", PostalCode = "68502" });
            unplaced = await props.CreateAsync(new PropertyInput { Line1 = "5 Unmapped Rd", City = "Lincoln", State = "NE", PostalCode = "68502" });
            await props.CreateAsync(new PropertyInput { Line1 = "3 Outside Rd", City = "Omaha", State = "NE", PostalCode = "68102" });
            var db = s.Get<OesDbContext>();
            (await db.Properties.SingleAsync(p => p.Id == near)).Latitude = 40.8136m;
            (await db.Properties.SingleAsync(p => p.Id == near)).Longitude = -96.7026m;
            (await db.Properties.SingleAsync(p => p.Id == far)).Latitude = 40.7400m;
            (await db.Properties.SingleAsync(p => p.Id == far)).Longitude = -96.6400m;
            await db.SaveChangesAsync();
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

        Assert.Contains((await Json(await api.GetAsync("/api/v1/storms"))).EnumerateArray(), x => x.GetProperty("id").GetGuid() == stormId);

        // Standing at 40.81, -96.70: the near house first, the far one next, the unmapped one last; Omaha isn't in the storm.
        var doors = (await Json(await api.GetAsync($"/api/v1/storms/{stormId}/canvass?lat=40.8130&lng=-96.7020"))).EnumerateArray().ToList();
        Assert.Equal([near, far, unplaced], doors.Select(d => d.GetProperty("propertyId").GetGuid()));
        Assert.True(doors[0].GetProperty("miles").GetDouble() < 0.1);
        Assert.True(doors[1].GetProperty("miles").GetDouble() > 4);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doors[2].GetProperty("miles").ValueKind);

        // A lead at the near door: tied to the storm and the house, which leaves the list.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/leads")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { firstName = "Door", lastName = "Answerer", phone = "(402) 555-0111", propertyId = near, stormEventId = stormId }),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var lead = await Json(await api.SendAsync(req));
        Assert.Equal(near, lead.GetProperty("propertyId").GetGuid());
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            var opp = await db.Opportunities.SingleAsync(o => o.Id == lead.GetProperty("opportunityId").GetGuid());
            Assert.Equal((stormId, near, true, "door_knock"), (opp.StormEventId!.Value, opp.PropertyId!.Value, opp.IsInsurance, opp.SourceKey));
            Assert.True(await db.PropertyContacts.AnyAsync(pc => pc.PropertyId == near && pc.ContactId == opp.PrimaryContactId));
        }
        doors = (await Json(await api.GetAsync($"/api/v1/storms/{stormId}/canvass"))).EnumerateArray().ToList();
        Assert.Equal(new[] { far, unplaced }.Order(), doors.Select(d => d.GetProperty("propertyId").GetGuid()).Order());
    }
}
