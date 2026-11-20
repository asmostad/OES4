using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Subcontractors;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Infrastructure.Security;
using Oes.Infrastructure.Subcontractors;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class SubcontractorTests(MySqlFixture fx)
{
    private sealed record Setup(Guid Tenant, AppUser Admin, Guid Trade, Guid Sub);

    private async Task<Setup> SetupAsync(string? subEmail = "crew@topcrew.example.com")
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "5 Sub St", City = "Lincoln", State = "NE", PostalCode = "68502", Notes = "Gate code 1234" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Home", LastName = "Owner", OwnerUserId = admin.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = admin.Id });
        var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing");
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
        var structure = (await s.Get<PropertyService>().GetAsync(property)).Structures.Single().Id;
        await est.AddSectionAsync(id, "Roof", structure, roofing.Id);
        var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "R", Name = "Roof", Unit = "SQ", DefaultPrice = 400m });
        await est.AddProductLineAsync(id, (await est.GetAsync(id)).Sections.Single(x => x.Section.Name == "Roof").Section.Id, product, 25);
        var v = await est.IssueAsync(id, null);
        await est.MarkSignedAsync(v, "Home Owner");
        var projectId = await s.Get<ProjectService>().CreateFromEstimateAsync(id);
        var trade = (await s.Get<ProjectService>().GetAsync(projectId)).Trades.Single().Trade.Id;

        var sub = await s.Get<SubcontractorService>().CreateAsync(new NewSubInput { OrganizationName = "Top Crew LLC", Email = subEmail, TradeCodes = ["roofing", "bogus"] });
        return new Setup(t.Id, admin, trade, sub);
    }

    [Fact]
    public async Task Subs_are_organization_contacts_with_effective_dated_rates_and_insurance()
    {
        var x = await SetupAsync();
        await using var s = fx.ScopeAs(x.Tenant, x.Admin);
        var subs = s.Get<SubcontractorService>();
        var d = await subs.GetAsync(x.Sub);
        Assert.True(d.Contact.IsOrganization);
        Assert.Null(d.Contact.OwnerUserId); // shared company record
        Assert.Equal(["roofing"], d.Sub.Trades); // unknown trade codes dropped

        await subs.AddRateAsync(x.Sub, null, "Tear-off & install", "sq", 85m, new DateOnly(2026, 1, 1));
        await subs.AddRateAsync(x.Sub, null, "Tear-off & install", "SQ", 92m, new DateOnly(2026, 6, 1));
        await Assert.ThrowsAsync<CrmRuleException>(() => subs.AddRateAsync(x.Sub, null, "Tear-off & install", "SQ", 95m, new DateOnly(2026, 3, 1)));
        Assert.Equal(85m, Assert.Single(await subs.RatesOnAsync(x.Sub, new DateOnly(2026, 5, 31))).Rate);
        Assert.Equal(92m, Assert.Single(await subs.RatesOnAsync(x.Sub, new DateOnly(2026, 6, 1))).Rate);
        Assert.Equal(new DateOnly(2026, 5, 31), (await subs.GetAsync(x.Sub)).Rates.Single(r => r.Rate == 85m).EffectiveTo);

        await Assert.ThrowsAsync<CrmRuleException>(() => subs.AddDocumentAsync(x.Sub, SubDocTypes.GeneralLiability, null, new MemoryStream([1]), "gl.pdf", "application/pdf"));
        await subs.AddDocumentAsync(x.Sub, SubDocTypes.GeneralLiability, new DateOnly(2027, 3, 1), new MemoryStream([1]), "gl.pdf", "application/pdf");
        await subs.AddDocumentAsync(x.Sub, SubDocTypes.WorkersComp, new DateOnly(2026, 12, 15), new MemoryStream([1]), "wc.pdf", "application/pdf");
        await subs.AddDocumentAsync(x.Sub, SubDocTypes.WorkersComp, new DateOnly(2027, 12, 15), new MemoryStream([1]), "wc2.pdf", "application/pdf");
        Assert.Equal(new DateOnly(2027, 3, 1), (await subs.GetAsync(x.Sub)).Sub.InsuranceExpiresOn); // earliest of the latest per type
    }

    [Fact]
    public async Task Work_order_flow_through_the_portal_link()
    {
        var x = await SetupAsync();
        string link;
        Guid woId;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            await s.Get<SubcontractorService>().AddRateAsync(x.Sub, null, "Tear-off & install", "SQ", 90m, new DateOnly(2026, 1, 1));
            var wos = s.Get<WorkOrderService>();
            woId = await wos.CreateAsync(x.Trade, x.Sub, new DateOnly(2026, 12, 3), "7:30 am", "Dumpster in driveway");
            await Assert.ThrowsAsync<CrmRuleException>(() => wos.IssueAsync(woId)); // no lines yet
            var rate = (await wos.GetAsync(woId)).Rates.Single();
            await wos.AddLineAsync(woId, rate.Id, null, 25, null, null);
            await wos.AddLineAsync(woId, null, "Haul-off", 1, "EA", 150m);
            Assert.Equal(2400m, (await wos.GetAsync(woId)).WorkOrder.Amount);

            var issued = await wos.IssueAsync(woId);
            Assert.True(issued.Emailed);
            link = issued.Link;
            await Assert.ThrowsAsync<CrmRuleException>(() => wos.AddLineAsync(woId, null, "More", 1, "EA", 10m)); // locked once issued

            // Only the hash is stored.
            var raw = link[(link.LastIndexOf('/') + 1)..];
            var db = s.Get<OesDbContext>();
            Assert.False(await db.ExternalAccessTokens.AnyAsync(t => t.TokenHash == raw));
            Assert.True(await db.ExternalAccessTokens.AnyAsync(t => t.TokenHash == AccessTokenService.Hash(raw)));
        }

        var token = link[(link.LastIndexOf('/') + 1)..];
        await using (var s = fx.ScopeFor(null)) // anonymous: no user, no tenant until the token sets it
        {
            var portal = s.Get<PortalService>();
            var view = await portal.GetAsync(token);
            Assert.NotNull(view);
            Assert.Equal("Top Crew LLC", view!.SubName);
            Assert.Equal("Gate code 1234", view.PropertyNotes);
            Assert.Equal(2, view.Lines.Count);
            await Assert.ThrowsAsync<CrmRuleException>(() => portal.CompleteAsync(token, null, [])); // must accept first
            Assert.True(await portal.RespondAsync(token, true, "Joe Crew", null));
        }
        await using (var s = fx.ScopeFor(null))
        {
            Assert.True(await s.Get<PortalService>().CompleteAsync(token, "Replaced 2 sheets of decking", [(new MemoryStream([0xFF, 0xD8, 0xFF]), "after.jpg", "image/jpeg")]));
        }
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var d = await s.Get<WorkOrderService>().GetAsync(woId);
            Assert.Equal(WorkOrderStatuses.Completed, d.WorkOrder.Status);
            Assert.Equal(AcceptStatuses.Accepted, d.WorkOrder.AcceptStatus);
            Assert.Equal("Joe Crew", d.WorkOrder.AcceptedName);
            // The photo rolls up to the trade and project.
            var onTrade = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Trade, x.Trade, TimelineScope.Everything, ItemTypes.Document);
            Assert.Contains(onTrade, i => i.FileName == "after.jpg");
            // The project owner hears about the sub's responses.
            var notes = await s.Get<Oes.Infrastructure.Work.NotificationService>().LatestAsync();
            Assert.Contains(notes, n => n.Kind == "work_order.accepted");
            Assert.Contains(notes, n => n.Kind == "work_order.completed" && n.Url == $"/WorkOrders/View/{woId}");
            await s.Get<WorkOrderService>().SetStatusAsync(woId, WorkOrderStatuses.Paid);
        }
    }

    [Fact]
    public async Task Links_stop_working_when_rotated_revised_cancelled_or_tampered()
    {
        var x = await SetupAsync(subEmail: null);
        string first, second;
        Guid woId;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var wos = s.Get<WorkOrderService>();
            woId = await wos.CreateAsync(x.Trade, x.Sub, new DateOnly(2026, 12, 3), null, null);
            await wos.AddLineAsync(woId, null, "Install", 1, "EA", 100m);
            var issued = await wos.IssueAsync(woId);
            Assert.False(issued.Emailed); // no email: the link is shown to copy instead
            first = issued.Link[(issued.Link.LastIndexOf('/') + 1)..];
            second = (await wos.NewLinkAsync(woId)).Link;
            second = second[(second.LastIndexOf('/') + 1)..];
        }
        await using (var s = fx.ScopeFor(null)) Assert.Null(await s.Get<PortalService>().GetAsync(first));
        await using (var s = fx.ScopeFor(null)) Assert.Null(await s.Get<PortalService>().GetAsync(second[..^1] + (second[^1] == 'A' ? 'B' : 'A')));
        await using (var s = fx.ScopeFor(null)) Assert.NotNull(await s.Get<PortalService>().GetAsync(second));

        await using (var s = fx.ScopeAs(x.Tenant, x.Admin)) await s.Get<WorkOrderService>().ReviseAsync(woId);
        await using (var s = fx.ScopeFor(null)) Assert.Null(await s.Get<PortalService>().GetAsync(second));

        string third;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var l = (await s.Get<WorkOrderService>().IssueAsync(woId)).Link;
            third = l[(l.LastIndexOf('/') + 1)..];
            await s.Get<WorkOrderService>().SetStatusAsync(woId, WorkOrderStatuses.Cancelled);
        }
        await using (var s = fx.ScopeFor(null)) Assert.Null(await s.Get<PortalService>().GetAsync(third));
    }

    [Fact]
    public async Task Portal_page_is_public_private_and_404s_bad_links()
    {
        var x = await SetupAsync();
        string link;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var wos = s.Get<WorkOrderService>();
            var woId = await wos.CreateAsync(x.Trade, x.Sub, new DateOnly(2026, 12, 3), null, null);
            await wos.AddLineAsync(woId, null, "Install", 1, "EA", 100m);
            link = (await wos.IssueAsync(woId)).Link;
        }
        var client = fx.App.Browser();
        var path = new Uri(link).AbsolutePath;
        var ok = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Top Crew LLC", await ok.Content.ReadAsStringAsync());
        Assert.Equal("no-referrer", ok.Headers.GetValues("Referrer-Policy").Single());

        var bad = await client.GetAsync("/portal/not-a-real-token");
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
        Assert.Contains("isn't available", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Issuing_from_the_page_saves_the_pdf_and_emails_it_to_the_sub()
    {
        var subEmail = $"crew{Guid.NewGuid():N}@topcrew.example.com";
        var x = await SetupAsync(subEmail);
        Guid woId;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var wos = s.Get<WorkOrderService>();
            woId = await wos.CreateAsync(x.Trade, x.Sub, new DateOnly(2026, 12, 3), "7:30 am", "Dumpster in driveway");
            await wos.AddLineAsync(woId, null, "Tear-off & install", 25, "SQ", 90m);
        }
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = x.Admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        async Task<string> Issue()
        {
            var page = await web.GetStringAsync($"/WorkOrders/View/{woId}");
            Assert.Contains($"action=\"/WorkOrders/Print/{woId}?handler=Issue\"", page);
            var r = await web.PostAsync($"/WorkOrders/Print/{woId}?handler=Issue", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(page) }));
            Assert.Equal($"/WorkOrders/View/{woId}", r.Headers.Location?.OriginalString);
            return await web.GetStringAsync($"/WorkOrders/View/{woId}");
        }
        string[] MailTo() => Directory.GetFiles(Path.Combine(fx.FilesRoot, "mail"), "*.html").Where(f => File.ReadAllText(f).Contains(subEmail)).ToArray();

        // Issued with the PDF: saved on the work order and attached to the sub's email.
        var after = await Issue();
        Assert.Contains($"Issued and emailed to {subEmail}. The PDF is saved on the work order.", after);
        Assert.Contains("data-issued-pdf", after);
        Assert.Contains("Dumpster in driveway", fx.Pdf.LastHtml);
        Guid pdfId;
        await using (var s = fx.ScopeAs(x.Tenant, x.Admin))
        {
            var wo = await s.Get<OesDbContext>().WorkOrders.SingleAsync(w => w.Id == woId);
            Assert.Equal(WorkOrderStatuses.Issued, wo.Status);
            pdfId = wo.PdfDocumentId!.Value;
            var doc = await s.Get<OesDbContext>().Documents.SingleAsync(d => d.Id == pdfId);
            Assert.Equal(("work_order", $"{wo.Number}.pdf"), (doc.CategoryKey, doc.FileName));
            var mail = Assert.Single(MailTo());
            Assert.Contains("The work order is attached as a PDF.", File.ReadAllText(mail));
            Assert.Equal("%PDF-1.7 stand-in", File.ReadAllText(mail[..^5] + $"-1-{wo.Number}.pdf"));
            await s.Get<WorkOrderService>().ReviseAsync(woId); // back to draft to issue again
        }

        // No browser on the server: still issued and emailed, without the attachment.
        fx.Pdf.Unavailable = true;
        try
        {
            Assert.Contains("No PDF was attached", await Issue());
            await using var s = fx.ScopeAs(x.Tenant, x.Admin);
            Assert.Null((await s.Get<OesDbContext>().WorkOrders.SingleAsync(w => w.Id == woId)).PdfDocumentId);
            Assert.Equal(2, MailTo().Length);
        }
        finally { fx.Pdf.Unavailable = false; }
    }
}
