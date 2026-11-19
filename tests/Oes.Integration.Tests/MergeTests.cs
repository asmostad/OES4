using Microsoft.EntityFrameworkCore;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class MergeTests(MySqlFixture fx)
{
    [Fact]
    public async Task Merging_contacts_moves_everything_and_opting_out_wins()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var contacts = s.Get<ContactService>();
        var db = s.Get<OesDbContext>();

        var keep = await contacts.CreateAsync(new ContactInput { FirstName = "Ann", LastName = "Lee", Methods = [new("email", "ann@example.test", null, true)] });
        var dup = await contacts.CreateAsync(new ContactInput
        {
            FirstName = "Ann", LastName = "Lee", Notes = "Prefers mornings", SourceKey = "referral",
            Methods = [new("email", "ANN@example.test", null, true), new("mobile", "402-555-0101", null, true)],
        });
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "3 Merge Ln", City = "Lincoln", State = "NE", PostalCode = "68502" });
        await s.Get<PropertyService>().LinkContactAsync(property, keep, "owner", true);
        await s.Get<PropertyService>().LinkContactAsync(property, dup, "owner", false);
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = dup, PropertyId = property, OwnerUserId = admin.Id });
        await s.Get<RecordItems>().AddNoteAsync(EntityTypes.Contact, dup, "Called about gutters");
        // The duplicate opted out of marketing email; the kept contact opted in afterwards.
        await s.Get<ConsentService>().RecordAsync(dup, Channels.Email, MessagePurposes.Marketing, ConsentStatuses.OptedOut, "verbal", null);
        await s.Get<ConsentService>().RecordAsync(keep, Channels.Email, MessagePurposes.Marketing, ConsentStatuses.OptedIn, "web_form", null);

        var merges = s.Get<MergeService>();
        Assert.Contains(await merges.SuggestContactsAsync(keep), x => x.Id == dup && x.Why.Contains("ann@example.test"));
        var preview = await merges.PreviewContactsAsync(keep, dup);
        Assert.Empty(preview.Blockers);
        Assert.Equal(1, preview.Moves.Single(m => m.Label == "Opportunities").Count);

        await merges.MergeContactsAsync(keep, dup);

        db.ChangeTracker.Clear();
        Assert.Equal(keep, (await db.Opportunities.SingleAsync(o => o.Id == opp)).PrimaryContactId);
        var methods = await db.ContactMethods.Where(m => m.ContactId == keep).ToListAsync();
        Assert.Equal(2, methods.Count); // the same email once, plus the mobile
        Assert.Single(methods, m => m.Kind == "email" && m.IsPrimary);
        Assert.Single(await db.PropertyContacts.Where(pc => pc.PropertyId == property).ToListAsync()); // the duplicate owner link is dropped
        Assert.True(await db.Notes.AnyAsync(n => n.EntityType == EntityTypes.Contact && n.EntityId == keep && n.Body == "Called about gutters"));
        var kept = await db.Contacts.SingleAsync(c => c.Id == keep);
        Assert.Equal(("Prefers mornings", "referral"), (kept.Notes, kept.SourceKey));
        var merged = await db.Contacts.SingleAsync(c => c.Id == dup);
        Assert.Equal(keep, merged.MergedIntoId);
        Assert.NotNull(merged.ArchivedAt);

        var marketing = (await s.Get<ConsentService>().CurrentAsync(keep)).Single(c => c.Channel == Channels.Email && c.Purpose == MessagePurposes.Marketing);
        Assert.Equal((ConsentStatuses.OptedOut, "merge"), (marketing.Status, marketing.Source));
        Assert.True(await db.ActivityEvents.AnyAsync(a => a.EntityId == keep && a.EventType == "contact.merged"));

        // The archived duplicate can't be merged again; a person can't be merged with an organization.
        await Assert.ThrowsAsync<RecordNotFoundException>(() => merges.MergeContactsAsync(keep, dup));
        var org = await contacts.CreateAsync(new ContactInput { Type = ContactTypes.Organization, OrganizationName = "Lee LLC" });
        await Assert.ThrowsAsync<CrmRuleException>(() => merges.MergeContactsAsync(keep, org));
    }

    [Fact]
    public async Task Merging_properties_moves_buildings_and_records_and_needs_permission()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid keep, dup, opp;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var properties = s.Get<PropertyService>();
            keep = await properties.CreateAsync(new PropertyInput { Line1 = "14 Oak St", City = "Lincoln", State = "NE", PostalCode = "68502" });
            dup = await properties.CreateAsync(new PropertyInput { Line1 = "14 Oak St", Line2 = "Rear", City = "Lincoln", State = "NE", PostalCode = "68502", Notes = "Dog in yard" });
            var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Bo", LastName = "Oak" });
            opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = dup, OwnerUserId = admin.Id });
            await s.Get<RecordItems>().AddNoteAsync(EntityTypes.Property, dup, "Gate code 1234");
        }

        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using (var r = fx.ScopeAs(t.Id, rep))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<MergeService>().MergePropertiesAsync(keep, dup));

        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            await s.Get<MergeService>().MergePropertiesAsync(keep, dup);
            var db = s.Get<OesDbContext>();
            db.ChangeTracker.Clear();
            var buildings = await db.PropertyStructures.Where(x => x.PropertyId == keep).OrderBy(x => x.Sort).Select(x => x.Name).ToListAsync();
            Assert.Equal(["Main House", "Main House (from 14 Oak St)"], buildings);
            Assert.Equal(keep, (await db.Opportunities.SingleAsync(o => o.Id == opp)).PropertyId);
            Assert.True(await db.Notes.AnyAsync(n => n.EntityId == keep && n.Body == "Gate code 1234"));
            var kept = await db.Properties.SingleAsync(p => p.Id == keep);
            Assert.Equal(("Rear", "Dog in yard"), (kept.Line2, kept.Notes));
            Assert.Equal(keep, (await db.Properties.SingleAsync(p => p.Id == dup)).MergedIntoId);
            // Rollups follow: the opportunity's note links now point at the kept property.
            Assert.False(await db.RecordLinks.AnyAsync(l => l.EntityType == EntityTypes.Property && l.EntityId == dup));
        }
    }
}
