using System.Net;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;
using Oes.Web.Controllers.Api;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class MessagingTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser Rep, Guid Contact, Guid Property, Guid Opportunity, string Mobile, string Company);

    private static int _n;

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        // A unique, valid-looking test number per world (555-01xx is reserved for fiction).
        var mobile = $"+1402555{Interlocked.Increment(ref _n) % 10000:0000}";
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "9 Message Ln", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput
        {
            FirstName = "Mia", LastName = "Message", OwnerUserId = rep.Id,
            Methods = [new MethodInput(ContactMethodKinds.Email, "Mia@Example.com", null, true), new MethodInput(ContactMethodKinds.Mobile, mobile, null, true)],
        });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = rep.Id });
        return new World(t.Id, admin, rep, contact, property, opp, mobile, t.Name);
    }


    [Fact]
    public async Task Email_carries_cc_bcc_and_attached_documents_the_sender_can_see()
    {
        var w = await WorldAsync();
        Guid doc, hidden, sent;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var items = s.Get<RecordItems>();
            doc = await items.UploadAsync(EntityTypes.Opportunity, w.Opportunity, new MemoryStream("%PDF-1.7 quote"u8.ToArray()), "Quote.pdf", "application/pdf", null);
            // A document on someone else's record the rep can't open.
            var other = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Not", LastName = "Theirs", OwnerUserId = w.Admin.Id });
            hidden = await items.UploadAsync(EntityTypes.Contact, other, new MemoryStream("secret"u8.ToArray()), "secret.txt", "text/plain", null);
            s.Get<OesDbContext>().Suppressions.Add(new Suppression { Channel = Channels.Email, Address = "gone@example.com", Scope = "all" });
            await s.Get<OesDbContext>().SaveChangesAsync();
        }

        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var messaging = s.Get<MessagingService>();
            EmailInput Input() => new()
            {
                EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = "mia@example.com",
                Subject = "Your quote", Body = "Attached.", Cc = "Adjuster@Example.com; adjuster@example.com, mia@example.com", Bcc = "office@example.com",
                DocumentIds = [doc, doc],
            };
            var r = await messaging.SendEmailAsync(Input());
            Assert.True(r.Ok);
            sent = r.MessageId;

            var bad = Input(); bad.Cc = "not an address";
            Assert.Contains("isn't an email address", (await Assert.ThrowsAsync<CrmRuleException>(() => messaging.SendEmailAsync(bad))).Message);
            bad = Input(); bad.Bcc = "gone@example.com";
            Assert.Contains("unsubscribed", (await Assert.ThrowsAsync<CrmRuleException>(() => messaging.SendEmailAsync(bad))).Message);
            bad = Input(); bad.DocumentIds = [hidden];
            await Assert.ThrowsAsync<RecordNotFoundException>(() => messaging.SendEmailAsync(bad));

            var m = await s.Get<OesDbContext>().EmailMessages.Include(x => x.Recipients).Include(x => x.Attachments).SingleAsync(x => x.Id == sent);
            Assert.Equal([("to", "mia@example.com"), ("cc", "adjuster@example.com"), ("bcc", "office@example.com")], m.Recipients.Select(x => (x.Kind, x.Address)));
            Assert.Equal(("Quote.pdf", doc), (m.Attachments.Single().FileName, m.Attachments.Single().DocumentId));

            // The provider got the file; the timeline and the message page show it.
            var id = m.ProviderMessageId![4..];
            var dir = Path.Combine(fx.FilesRoot, "messages", "email");
            Assert.Equal("%PDF-1.7 quote", await File.ReadAllTextAsync(Path.Combine(dir, $"{id}-1-Quote.pdf")));
            Assert.Contains("Bcc: office@example.com", await File.ReadAllTextAsync(Path.Combine(dir, id + ".html")));
            var item = (await s.Get<RecordItems>().TimelineAsync(EntityTypes.Opportunity, w.Opportunity, TimelineScope.Everything, null, 20)).Single(i => i.Id == sent);
            Assert.Equal("mia@example.com", item.Message!.Counterparty);
            Assert.Equal(doc, item.Message.Attachments.Single().DocumentId);
            var view = await messaging.GetAsync(ItemTypes.Email, sent);
            Assert.Equal(("mia@example.com", "adjuster@example.com", "office@example.com", 1), (view.Counterparty, view.Cc, view.Bcc, view.Attachments.Count));
            Assert.Equal([doc], (await s.Get<RecordItems>().AttachableDocumentsAsync(EntityTypes.Opportunity, w.Opportunity)).Select(d => d.Id));
        }

        // The record's email form offers the record's files; ticking one attaches it.
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = w.Rep.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync($"/Opportunities/View/{w.Opportunity}");
        Assert.Contains($"name=\"email.DocumentIds\" value=\"{doc}\"", page);
        Assert.DoesNotContain(hidden.ToString(), page);
        var post = await web.PostAsync("/Messages/Send?handler=Email", new FormUrlEncodedContent(
        [
            new("email.EntityType", EntityTypes.Opportunity), new("email.EntityId", w.Opportunity.ToString()), new("email.ContactId", w.Contact.ToString()),
            new("email.To", "mia@example.com"), new("email.Subject", "From the form"), new("email.Body", "See attached."),
            new("email.DocumentIds", doc.ToString()), new("returnUrl", $"/Opportunities/View/{w.Opportunity}"), new("__RequestVerificationToken", Token(page)),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
            Assert.Equal(doc, (await s.Get<OesDbContext>().EmailMessages.Include(x => x.Attachments).SingleAsync(x => x.Subject == "From the form")).Attachments.Single().DocumentId);
    }
    private static string CompanyNumber(World w) => "+1531555" + w.Mobile[^4..];

    [Fact]
    public async Task Templates_validate_merge_fields_and_need_permission()
    {
        var w = await WorldAsync();
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var t = s.Get<TemplateService>();
            Assert.NotEmpty(await t.ListAsync(Channels.Sms)); // starter templates
            var bad = await Assert.ThrowsAsync<CrmRuleException>(() => t.SaveAsync(null, new TemplateInput { Channel = Channels.Email, Name = "Typo", Subject = "Hi", Body = "Hi {{contact.frist_name}}" }));
            Assert.Contains("contact.frist_name", bad.Message);
            await Assert.ThrowsAsync<CrmRuleException>(() => t.SaveAsync(null, new TemplateInput { Channel = Channels.Email, Name = "No subject", Body = "x" }));
            var id = await t.SaveAsync(null, new TemplateInput { Channel = Channels.Sms, Name = "Hello", Body = "Hi {{contact.first_name}}", Subject = "ignored" });
            Assert.Null((await t.GetAsync(id)).Subject);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<TemplateService>().SaveAsync(null, new TemplateInput { Channel = Channels.Sms, Name = "Mine", Body = "x" }));
    }

    [Fact]
    public async Task Email_merges_fields_encodes_html_and_rolls_up_but_stays_private_to_the_sender()
    {
        var w = await WorldAsync();
        Guid fromRep, fromAdmin;
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var r = await s.Get<MessagingService>().SendEmailAsync(new EmailInput
            {
                EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = "mia@example.com",
                Subject = "Your roof at {{property.street}}", Body = "Hi {{contact.first_name}},\n<script>alert(1)</script>\n{{company.name}}",
            });
            Assert.True(r.Ok);
            fromRep = r.MessageId;
            var m = await s.Get<OesDbContext>().EmailMessages.Include(x => x.Recipients).SingleAsync(x => x.Id == fromRep);
            Assert.Equal("Your roof at 9 Message Ln", m.Subject.Trim());
            Assert.StartsWith("Hi Mia,", m.Body);
            Assert.Contains(w.Company, m.Body);
            Assert.Equal(w.Contact, m.Recipients.Single().ContactId);
            Assert.Contains("&lt;script&gt;", MessagingService.ToHtml(m.Body));
            Assert.DoesNotContain("<script>", MessagingService.ToHtml(m.Body));

            // Only addresses on the record's contacts can be used.
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().SendEmailAsync(new EmailInput
            { EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = "someone@else.com", Subject = "x", Body = "x" }));
        }
        var picked = Directory.GetFiles(Path.Combine(fx.FilesRoot, "messages", "email"), "*.html").Select(File.ReadAllText).Single(f => f.Contains("Message Ln"));
        Assert.DoesNotContain("<script>", picked);

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            fromAdmin = (await s.Get<MessagingService>().SendEmailAsync(new EmailInput
            { EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = "mia@example.com", Subject = "From the office", Body = "Hello" })).MessageId;
            // Rolls up to the contact and property; the admin (email.view_all) sees both.
            var onProperty = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Property, w.Property, TimelineScope.Everything, ItemTypes.Email);
            Assert.Equal(2, onProperty.Count);
            Assert.All(onProperty, i => Assert.Equal(EntityTypes.Opportunity, i.EntityType));
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var mine = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Contact, w.Contact, TimelineScope.Everything, ItemTypes.Email);
            Assert.Equal([fromRep], mine.Select(i => i.Id)); // teammates' outgoing email stays private
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<MessagingService>().GetAsync(ItemTypes.Email, fromAdmin));
        }
    }

    [Fact]
    public async Task Email_respects_opt_out()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Rep);
        await s.Get<ConsentService>().RecordAsync(w.Contact, Channels.Email, MessagePurposes.Transactional, ConsentStatuses.OptedOut, "verbal", "Asked by phone");
        var ex = await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().SendEmailAsync(new EmailInput
        { EntityType = EntityTypes.Contact, EntityId = w.Contact, ContactId = w.Contact, To = "mia@example.com", Subject = "x", Body = "x" }));
        Assert.Contains("opted out", ex.Message);
    }

    [Fact]
    public async Task Texts_need_a_number_and_an_opt_in_and_the_first_one_says_how_to_stop()
    {
        var w = await WorldAsync();
        var input = new SmsInput { EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = w.Mobile, Body = "Hi {{contact.first_name}}, see you at 9" };
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            Assert.Contains("isn't set up", (await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().SendSmsAsync(input))).Message);
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<MessagingService>().SetPhoneNumberAsync(CompanyNumber(w))); // settings.manage
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) await s.Get<MessagingService>().SetPhoneNumberAsync(CompanyNumber(w));
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var msg = s.Get<MessagingService>();
            Assert.Contains("opt-in", (await Assert.ThrowsAsync<CrmRuleException>(() => msg.SendSmsAsync(input))).Message);
            await s.Get<ConsentService>().RecordAsync(w.Contact, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "verbal", "At the inspection");
            var first = await msg.SendSmsAsync(input);
            var second = await msg.SendSmsAsync(input);
            var db = s.Get<OesDbContext>();
            Assert.Equal("Hi Mia, see you at 9\n" + MessagingService.OptOutFooter, (await db.SmsMessages.SingleAsync(m => m.Id == first.MessageId)).Body);
            Assert.Equal("Hi Mia, see you at 9", (await db.SmsMessages.SingleAsync(m => m.Id == second.MessageId)).Body);
            Assert.Equal(CompanyNumber(w), (await db.SmsMessages.SingleAsync(m => m.Id == first.MessageId)).FromNumber);
        }
        // Another company can't claim the same number.
        var (other, otherAdmin) = await fx.CreateTenantAsync();
        await using (var s = fx.ScopeAs(other.Id, otherAdmin))
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().SetPhoneNumberAsync(CompanyNumber(w)));
    }

    [Fact]
    public async Task Inbound_texts_route_by_number_and_STOP_and_START_are_honored()
    {
        var w = await WorldAsync();
        var input = new SmsInput { EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, ContactId = w.Contact, To = w.Mobile, Body = "Running late" };
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) await s.Get<MessagingService>().SetPhoneNumberAsync(CompanyNumber(w));
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            await s.Get<ConsentService>().RecordAsync(w.Contact, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "written", null);
            await s.Get<MessagingService>().SendSmsAsync(input);
        }

        var client = fx.App.Browser();
        async Task<HttpStatusCode> Post(string body, string? secret = "test-webhook-secret", string? sid = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sms/dev")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["From"] = w.Mobile, ["To"] = CompanyNumber(w), ["Body"] = body, ["MessageSid"] = sid ?? Guid.NewGuid().ToString("N") }),
            };
            if (secret is not null) req.Headers.Add(SmsWebhookController.SecretHeader, secret);
            return (await client.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Post("hi", secret: "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Post("hi", secret: null));
        Assert.Equal(HttpStatusCode.OK, await Post("Can you come Tuesday?", sid: "SM-reply-1"));
        Assert.Equal(HttpStatusCode.OK, await Post("Can you come Tuesday?", sid: "SM-reply-1")); // provider retry

        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            // The reply continues on the opportunity the last text came from, and pings the sender.
            var onOpp = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Opportunity, w.Opportunity, TimelineScope.ThisRecord, ItemTypes.Sms);
            var reply = Assert.Single(onOpp, i => i.Message!.Direction == Directions.Inbound);
            Assert.Equal("Can you come Tuesday?", reply.Text);
            Assert.Equal("Mia Message", reply.AuthorName);
            Assert.Contains(await s.Get<NotificationService>().LatestAsync(), n => n.Kind == "sms.received" && n.Url == $"/Opportunities/View/{w.Opportunity}");
        }

        Assert.Equal(HttpStatusCode.OK, await Post("Stop"));
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var db = s.Get<OesDbContext>();
            Assert.True(await db.Suppressions.AnyAsync(x => x.Address == w.Mobile && x.Reason == SuppressionReasons.Stop && x.RemovedAt == null));
            Assert.Contains("unsubscribed", (await db.SmsMessages.OrderByDescending(m => m.At).ThenByDescending(m => m.Id).FirstAsync(m => m.ToNumber == w.Mobile)).Body);
            Assert.All((await s.Get<ConsentService>().CurrentAsync(w.Contact)).Where(c => c.Channel == Channels.Sms), c => Assert.Equal(ConsentStatuses.OptedOut, c.Status));
            Assert.Contains("STOP", (await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MessagingService>().SendSmsAsync(input))).Message);
            // Staff can't undo a STOP.
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<ConsentService>().RecordAsync(w.Contact, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "verbal", null));
        }

        Assert.Equal(HttpStatusCode.OK, await Post("START"));
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
            Assert.True((await s.Get<MessagingService>().SendSmsAsync(input)).Ok);

        // A number no company owns: same answer, nothing stored.
        var unknown = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sms/dev")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["From"] = w.Mobile, ["To"] = "+15315550000", ["Body"] = "hello" }),
        };
        unknown.Headers.Add(SmsWebhookController.SecretHeader, "test-webhook-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(unknown)).StatusCode);
    }
}
