using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

/// <summary>CRM core rules against real MySQL: visibility, duplicates, rollups, pipeline.</summary>
[Collection(DbCollection.Name)]
public sealed class CrmTests(MySqlFixture fx)
{
    private static ContactInput Person(string first, string last, Guid? owner, string? email = null) => new()
    {
        FirstName = first, LastName = last, OwnerUserId = owner,
        Methods = email is null ? [] : [new MethodInput(ContactMethodKinds.Email, email, null, true)],
    };

    private static PropertyInput Address(string line1, string zip = "68502") =>
        new() { Line1 = line1, City = "Lincoln", State = "NE", PostalCode = zip };

    private async Task<(Guid Tenant, AppUser Admin, AppUser RepA, AppUser RepB)> TeamAsync()
    {
        var (tenant, admin) = await fx.CreateTenantAsync();
        var repA = await fx.AddMemberAsync(tenant.Id, "Sales Rep");
        var repB = await fx.AddMemberAsync(tenant.Id, "Sales Rep");
        return (tenant.Id, admin, repA, repB);
    }

    [Fact]
    public async Task New_tenants_get_pipeline_stages_and_pick_lists()
    {
        var (tenant, _) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeFor(tenant.Id);
        var db = s.Get<OesDbContext>();
        Assert.Equal(TenantDefaults.Stages.Length, await db.PipelineStages.CountAsync());
        Assert.Single(await db.PipelineStages.Where(x => x.IsDefault).ToListAsync());
        Assert.True(await db.LookupValues.AnyAsync(l => l.Type == LookupTypes.LeadSource));
    }

    [Fact]
    public async Task Reps_see_their_own_and_unassigned_records_but_not_each_others()
    {
        var (tenant, admin, repA, repB) = await TeamAsync();
        Guid aContact, poolContact, bContact;
        await using (var s = fx.ScopeAs(tenant, repA)) aContact = await s.Get<ContactService>().CreateAsync(Person("Ann", "Alpha", repA.Id));
        await using (var s = fx.ScopeAs(tenant, repA)) poolContact = await s.Get<ContactService>().CreateAsync(Person("Pool", "Lead", null));
        await using (var s = fx.ScopeAs(tenant, repB)) bContact = await s.Get<ContactService>().CreateAsync(Person("Bob", "Bravo", repB.Id));

        await using (var s = fx.ScopeAs(tenant, repA))
        {
            var visible = (await s.Get<ContactService>().SearchAsync(null, null, null)).Items.Select(c => c.Id).ToList();
            Assert.Contains(aContact, visible);
            Assert.Contains(poolContact, visible);
            Assert.DoesNotContain(bContact, visible);
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<ContactService>().GetAsync(bContact));
            Assert.DoesNotContain(await s.Get<CrmLookups>().SearchAsync("Bravo"), h => h.Id == bContact);
        }

        await using (var s = fx.ScopeAs(tenant, admin))
        {
            var visible = (await s.Get<ContactService>().SearchAsync(null, null, null)).Items.Select(c => c.Id).ToList();
            Assert.Contains(bContact, visible);
        }
    }

    [Fact]
    public async Task Reps_cannot_edit_or_reassign_someone_elses_record()
    {
        var (tenant, admin, repA, repB) = await TeamAsync();
        Guid contactId;
        await using (var s = fx.ScopeAs(tenant, admin)) contactId = await s.Get<ContactService>().CreateAsync(Person("Owned", "ByB", repB.Id));

        await using (var s = fx.ScopeAs(tenant, repB))
        {
            // Reps can edit their own record but not hand it to someone else.
            await s.Get<ContactService>().UpdateAsync(contactId, Person("Owned", "Renamed", repB.Id));
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<ContactService>().UpdateAsync(contactId, Person("Owned", "Renamed", repA.Id)));
        }
        await using (var s = fx.ScopeAs(tenant, repA))
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<ContactService>().UpdateAsync(contactId, Person("Hijack", "Attempt", repA.Id)));
    }

    [Fact]
    public async Task The_same_address_typed_differently_is_caught_as_a_duplicate()
    {
        var (tenant, admin, _, _) = await TeamAsync();
        await using var s = fx.ScopeAs(tenant, admin);
        var svc = s.Get<PropertyService>();
        var id = await svc.CreateAsync(Address("123 North Main Street"));

        var dup = await Assert.ThrowsAsync<DuplicatePropertyException>(() => svc.CreateAsync(Address("123 n. main st")));
        Assert.Equal(id, dup.ExistingId);

        var matches = await svc.FindMatchesAsync(Address("123 N Main Street"));
        Assert.Contains(matches, m => m.Exact && m.Id == id);
        Assert.Contains(await svc.FindMatchesAsync(Address("123 N Maine St")), m => !m.Exact && m.Id == id);
    }

    [Fact]
    public async Task Duplicate_check_spans_reps_without_revealing_the_address()
    {
        var (tenant, _, repA, repB) = await TeamAsync();
        await using (var s = fx.ScopeAs(tenant, repA))
        {
            var c = await s.Get<ContactService>().CreateAsync(Person("Hidden", "Owner", repA.Id));
            var p = await s.Get<PropertyService>().CreateAsync(Address("9 Secret Ln", "68510"));
            await s.Get<PropertyService>().LinkContactAsync(p, c, "owner", true);
        }
        await using (var s = fx.ScopeAs(tenant, repB))
        {
            var matches = await s.Get<PropertyService>().FindMatchesAsync(Address("9 Secret Lane", "68510"));
            var exact = Assert.Single(matches, m => m.Exact);
            Assert.DoesNotContain("Secret", exact.Address);
            await Assert.ThrowsAsync<DuplicatePropertyException>(() => s.Get<PropertyService>().CreateAsync(Address("9 secret ln", "68510")));
        }
    }

    [Fact]
    public async Task Lead_without_address_then_attached_rolls_its_notes_up_to_the_property()
    {
        var (tenant, admin, _, _) = await TeamAsync();
        await using var s = fx.ScopeAs(tenant, admin);
        var contactId = await s.Get<ContactService>().CreateAsync(Person("Rolly", "Up", admin.Id));
        var oppId = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contactId, OwnerUserId = admin.Id, IsInsurance = true, PerilKey = "hail" });
        var items = s.Get<RecordItems>();
        await items.AddNoteAsync(EntityTypes.Opportunity, oppId, "Called, inspection Friday");

        var propertyId = await s.Get<OpportunityService>().AttachNewPropertyAsync(oppId, Address("500 Rollup Rd"));

        var everything = await items.TimelineAsync(EntityTypes.Property, propertyId, TimelineScope.Everything, ItemTypes.Note);
        Assert.Contains(everything, i => i.Text == "Called, inspection Friday");
        var thisOnly = await items.TimelineAsync(EntityTypes.Property, propertyId, TimelineScope.ThisRecord, ItemTypes.Note);
        Assert.DoesNotContain(thisOnly, i => i.Text == "Called, inspection Friday");

        // Attaching links the lead's contact to the property as owner.
        var detail = await s.Get<PropertyService>().GetAsync(propertyId);
        Assert.Contains(detail.Contacts, c => c.ContactId == contactId && c.IsCurrent && c.IsPrimary);

        // And the note is on the contact too.
        var onContact = await items.TimelineAsync(EntityTypes.Contact, contactId, TimelineScope.Everything, ItemTypes.Note);
        Assert.Contains(onContact, i => i.Text == "Called, inspection Friday");
    }

    [Fact]
    public async Task Pipeline_rules_numbering_lost_reason_and_contact_status()
    {
        var (tenant, admin, _, _) = await TeamAsync();
        await using var s = fx.ScopeAs(tenant, admin);
        var opps = s.Get<OpportunityService>();
        var contactId = await s.Get<ContactService>().CreateAsync(Person("Win", "Ning", admin.Id));
        var id1 = await opps.CreateAsync(new OpportunityInput { PrimaryContactId = contactId, OwnerUserId = admin.Id });
        var id2 = await opps.CreateAsync(new OpportunityInput { PrimaryContactId = contactId, OwnerUserId = admin.Id });

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"OPP-{year}-0001", (await opps.GetAsync(id1)).Opportunity.Number);
        Assert.Equal($"OPP-{year}-0002", (await opps.GetAsync(id2)).Opportunity.Number);

        var stages = await opps.StagesAsync();
        var lost = stages.Single(x => x.Outcome == Outcomes.Lost);
        var won = stages.Single(x => x.Outcome == Outcomes.Won);

        await Assert.ThrowsAsync<CrmRuleException>(() => opps.MoveStageAsync(id2, lost.Id, null));
        await opps.MoveStageAsync(id2, lost.Id, null, "price");
        Assert.Equal(Outcomes.Lost, (await opps.GetAsync(id2)).Opportunity.Outcome);

        await opps.MoveStageAsync(id1, won.Id, "Signed");
        var d = await opps.GetAsync(id1);
        Assert.Equal(Outcomes.Won, d.Opportunity.Outcome);
        Assert.NotNull(d.Opportunity.WonAt);
        Assert.Equal(2, d.History.Count);
        Assert.Equal(ContactStatuses.Customer, (await s.Get<ContactService>().GetAsync(contactId)).Contact.Status);
    }

    [Fact]
    public async Task Documents_are_only_downloadable_by_users_who_can_see_the_record()
    {
        var (tenant, _, repA, repB) = await TeamAsync();
        Guid docId;
        await using (var s = fx.ScopeAs(tenant, repA))
        {
            var c = await s.Get<ContactService>().CreateAsync(Person("Doc", "Owner", repA.Id));
            docId = await s.Get<RecordItems>().UploadAsync(EntityTypes.Contact, c, new MemoryStream("contract"u8.ToArray()), "contract.pdf", "application/pdf", "contract");
            var (doc, content) = await s.Get<RecordItems>().OpenDocumentAsync(docId);
            await using (content) Assert.Equal("contract", await new StreamReader(content).ReadToEndAsync());
            Assert.Equal(64, doc.Sha256!.Length);
        }
        await using (var s = fx.ScopeAs(tenant, repB))
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<RecordItems>().OpenDocumentAsync(docId));
    }

    [Fact]
    public async Task Organizations_list_their_people()
    {
        var (tenant, admin, _, _) = await TeamAsync();
        await using var s = fx.ScopeAs(tenant, admin);
        var svc = s.Get<ContactService>();
        var org = await svc.CreateAsync(new ContactInput { Type = ContactTypes.Organization, OrganizationName = "Acme PM", OwnerUserId = admin.Id });
        var input = Person("Dana", "Manager", admin.Id);
        input.OrganizationId = org;
        input.Title = "Portfolio Manager";
        var person = await svc.CreateAsync(input);

        var orgDetail = await svc.GetAsync(org);
        var row = Assert.Single(orgDetail.People);
        Assert.Equal(person, row.OtherContactId);
        Assert.True(row.IsPrimary);
        Assert.Contains((await svc.GetAsync(person)).Organizations, o => o.OtherContactId == org);
    }
}
