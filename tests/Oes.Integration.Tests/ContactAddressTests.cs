using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ContactAddressTests(MySqlFixture fx)
{
    private static AddressInput Address(string line1, string city = "Omaha") => new() { Line1 = line1, City = city, State = "ne", PostalCode = "68102" };

    [Fact]
    public async Task Contacts_have_one_mailing_and_one_billing_address_used_for_mail()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var contacts = s.Get<ContactService>();
        var fields = s.Get<MergeFields>();

        var id = await contacts.CreateAsync(new ContactInput { FirstName = "Ida", LastName = "Away" });
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "9 Rental Rd", City = "Lincoln", State = "NE", PostalCode = "68502" });
        await s.Get<PropertyService>().LinkContactAsync(property, id, "owner", true);
        async Task<string?> Mailing() => (await fields.ForAsync(EntityTypes.Contact, id, null)).GetValueOrDefault("contact.mailing_address");

        Assert.Equal("9 Rental Rd, Lincoln, NE 68502", await Mailing()); // no address of their own: the property

        await Assert.ThrowsAsync<CrmRuleException>(() => contacts.SaveAddressAsync(id, ContactAddressKinds.Mailing, new AddressInput { Line1 = "1 Main" }));
        await Assert.ThrowsAsync<CrmRuleException>(() => contacts.SaveAddressAsync(id, "summer", Address("1 Main")));
        await contacts.SaveAddressAsync(id, ContactAddressKinds.Billing, Address("PO Box 12"));
        await contacts.SaveAddressAsync(id, ContactAddressKinds.Mailing, Address("1 Main St"));
        await contacts.SaveAddressAsync(id, ContactAddressKinds.Mailing, Address("2 Main St")); // replaces, one per kind

        var detail = await contacts.GetAsync(id);
        Assert.Equal(["2 Main St, Omaha, NE 68102", "PO Box 12, Omaha, NE 68102"], detail.Addresses.Select(a => a.SingleLine));
        Assert.Equal("2 Main St, Omaha, NE 68102", await Mailing());

        await contacts.RemoveAddressAsync(id, ContactAddressKinds.Mailing);
        Assert.Equal("PO Box 12, Omaha, NE 68102", await Mailing());
        Assert.True(await s.Get<OesDbContext>().ActivityEvents.AnyAsync(a => a.EntityId == id && a.EventType == "contact.address_saved"));
    }

    [Fact]
    public async Task Merging_keeps_the_kept_contacts_address_and_fills_a_missing_kind()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var contacts = s.Get<ContactService>();
        var keep = await contacts.CreateAsync(new ContactInput { FirstName = "Jo", LastName = "Same" });
        var dup = await contacts.CreateAsync(new ContactInput { FirstName = "Jo", LastName = "Same" });
        await contacts.SaveAddressAsync(keep, ContactAddressKinds.Mailing, Address("10 Keep Ave"));
        await contacts.SaveAddressAsync(dup, ContactAddressKinds.Mailing, Address("20 Dup Ave"));
        await contacts.SaveAddressAsync(dup, ContactAddressKinds.Billing, Address("30 Bill Ave"));

        var merges = s.Get<MergeService>();
        Assert.Equal(2, (await merges.PreviewContactsAsync(keep, dup)).Moves.Single(m => m.Label == "Mailing and billing addresses").Count);
        await merges.MergeContactsAsync(keep, dup);

        var db = s.Get<OesDbContext>();
        db.ChangeTracker.Clear();
        var kept = await db.ContactAddresses.Where(a => a.ContactId == keep).OrderBy(a => a.Kind).Select(a => a.Kind + ":" + a.Line1).ToListAsync();
        Assert.Equal(["billing:30 Bill Ave", "mailing:10 Keep Ave"], kept);
        Assert.False(await db.ContactAddresses.AnyAsync(a => a.ContactId == dup));
    }

    [Fact]
    public async Task The_contact_page_adds_and_shows_an_address()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid id;
        await using (var s = fx.ScopeAs(t.Id, admin)) id = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Web", LastName = "Mail" });

        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync($"/Contacts/View/{id}");
        Assert.Contains("Mail goes to their property", page);
        Assert.Contains("Add mailing address", page);

        var res = await web.PostAsync($"/Contacts/View/{id}?handler=SaveAddress", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["kind"] = "mailing", ["address.Line1"] = "5 Post Rd", ["address.City"] = "Omaha", ["address.State"] = "NE", ["address.PostalCode"] = "68102",
            ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, res.StatusCode);
        page = await web.GetStringAsync($"/Contacts/View/{id}");
        Assert.Contains("5 Post Rd, Omaha, NE 68102", page);
        Assert.Contains("Add billing address", page);
    }

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
