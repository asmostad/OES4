using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class UnmatchedTextTests(MySqlFixture fx)
{
    private static string Number(string prefix) => prefix + Random.Shared.Next(1000000, 9999999);

    [Fact]
    public async Task Texts_from_unknown_numbers_wait_in_an_inbox_until_filed_or_dismissed()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var company = Number("+1532");
        var stranger = Number("+1402");
        var newbie = Number("+1402");
        var spam = Number("+1800");
        await using (var s = fx.ScopeAs(t.Id, admin)) await s.Get<MessagingService>().SetPhoneNumberAsync(company);
        async Task Receive(string from, string body) { await using var s = fx.ScopeFor(null); Assert.True(await s.Get<MessagingService>().ReceiveSmsAsync(from, company, body, null)); }

        await Receive(stranger, "Is this the roofer? Hail hit us yesterday");
        await Receive(stranger, "Address is 9 Elm");
        await Receive(newbie, "Need a quote for gutters");
        await Receive(spam, "WIN A PRIZE");
        await Receive(spam, "STOP");

        Guid contact;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var texts = s.Get<UnmatchedTextService>();
            var threads = await texts.ListAsync();
            Assert.Equal(3, threads.Count);
            Assert.Equal(2, threads.Single(x => x.Number == ContactValue.Phone(stranger)).Unread);
            Assert.True(await s.Get<OesDbContext>().Notifications.AnyAsync(n => n.UserId == admin.Id && n.Kind == "sms.unmatched"));

            // Reply before filing: the first reply says how to opt out; later ones don't; a number that never texted can't be texted.
            var messaging = s.Get<MessagingService>();
            Assert.True((await messaging.ReplyToUnknownAsync(stranger, "Yes, this is the roofer. What's the address?")).Ok);
            await messaging.ReplyToUnknownAsync(stranger, "Thanks!");
            var replies = await s.Get<OesDbContext>().SmsMessages.Where(m => m.ToNumber == ContactValue.Phone(stranger) && m.SentByUserId == admin.Id).OrderBy(m => m.At).ToListAsync();
            Assert.Equal(2, replies.Count);
            Assert.EndsWith(MessagingService.OptOutFooter, replies[0].Body);
            Assert.Equal("Thanks!", replies[1].Body);
            Assert.Equal(4, (await texts.ListAsync()).Single(x => x.Number == ContactValue.Phone(stranger)).Messages.Count); // the conversation shows both sides
            await Assert.ThrowsAsync<CrmRuleException>(() => messaging.ReplyToUnknownAsync(Number("+1402"), "Hi"));
            Assert.Contains("STOP", (await Assert.ThrowsAsync<CrmRuleException>(() => messaging.ReplyToUnknownAsync(spam, "Hi"))).Message);

            // File on an existing contact: the number is added, so the next text matches by itself.
            contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Ella", LastName = "Elm" });
            await texts.LinkAsync(stranger, contact);
            await texts.DismissAsync(spam);
            var lead = await texts.NewLeadAsync(newbie, "Gus", "Gutter", createOpportunity: true);
            Assert.NotNull(lead.OpportunityId);
            Assert.Empty(await texts.ListAsync());
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().ReplyToUnknownAsync(stranger, "Hello again")); // on a contact now
            Assert.Equal(0, await texts.CountAsync());

            var db = s.Get<OesDbContext>();
            Assert.True(await db.ContactMethods.AnyAsync(m => m.ContactId == contact && m.NormalizedValue == ContactValue.Phone(stranger) && m.IsPrimary));
            Assert.Equal(4, await db.SmsMessages.CountAsync(m => m.ContactId == contact)); // their 2 texts and our 2 replies
            Assert.True(await db.RecordLinks.AnyAsync(l => l.ItemType == "sms" && l.EntityType == EntityTypes.Opportunity && l.EntityId == lead.OpportunityId));
            Assert.Equal("phone_in", (await db.Opportunities.SingleAsync(o => o.Id == lead.OpportunityId)).SourceKey);
        }

        await Receive(stranger, "Any update?");
        await Receive(spam, "WIN AGAIN");
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(contact, (await db.SmsMessages.OrderByDescending(m => m.At).FirstAsync(m => m.Body == "Any update?")).ContactId);
            Assert.Equal([ContactValue.Phone(spam)], (await s.Get<UnmatchedTextService>().ListAsync()).Select(x => x.Number)); // a new text brings it back
        }

        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using (var r = fx.ScopeAs(t.Id, rep))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<UnmatchedTextService>().ListAsync());

        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await web.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync("/Messages/Unmatched");
        Assert.Contains("WIN AGAIN", page);
        Assert.Contains("File here", page);
    }
}
