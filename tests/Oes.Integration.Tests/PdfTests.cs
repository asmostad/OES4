using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Documents;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Production;
using Oes.Infrastructure.Subcontractors;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class PdfTests(MySqlFixture fx)
{
    [Fact]
    public async Task Print_documents_download_as_self_contained_pdfs()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid versionId, coId, tradeId, woId, contactId, estimateId, projectId;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "12 Paper Ln", City = "Lincoln", State = "NE", PostalCode = "68502" });
            var contact = contactId = await s.Get<ContactService>().CreateAsync(new ContactInput
            {
                FirstName = "Penny", LastName = "Dee-Eff", Methods = [new MethodInput(ContactMethodKinds.Email, "penny@example.com", null, true)],
            });
            var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property });
            var est = s.Get<EstimateService>();
            var id = estimateId = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
            var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
            await est.UpdateSectionAsync(section, "Roof", "- Tear off to the deck.\n- Install <new> shingles & ridge cap.");
            var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "SQ", Name = "Roof per square", Unit = "SQ", DefaultPrice = 400m });
            await est.AddProductLineAsync(id, section, product, 25);
            versionId = await est.IssueAsync(id, null);
            await est.MarkSignedAsync(versionId, "Penny Dee-Eff");

            var projects = s.Get<ProjectService>();
            projectId = await projects.CreateFromEstimateAsync(id);
            tradeId = (await projects.GetAsync(projectId)).Trades.Single().Trade.Id;
            coId = await projects.AddChangeOrderAsync(projectId, null, "Replace rotted decking", 285m, "Found at tear-off", false);
            var sub = await s.Get<SubcontractorService>().CreateAsync(new NewSubInput { OrganizationName = "Paper Crew LLC", Email = $"crew{Guid.NewGuid():N}@example.com", TradeCodes = ["roofing"] });
            woId = await s.Get<WorkOrderService>().CreateAsync(tradeId, sub, new DateOnly(2026, 12, 3), "7:30 am", "Dumpster in driveway");
        }

        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await web.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await web.GetAsync("/Account/SelectTenant");

        // The print views offer the download.
        Assert.Contains("?handler=Pdf", await web.GetStringAsync($"/Estimates/Proposal/{versionId}"));

        async Task<string> Download(string url, string expectedName)
        {
            var r = await web.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
            Assert.Matches(expectedName, r.Content.Headers.ContentDisposition?.FileName ?? "");
            Assert.StartsWith("%PDF", await r.Content.ReadAsStringAsync());
            var html = fx.Pdf.LastHtml!;
            // Self-contained: inline styles, nothing to fetch, no scripts, no toolbar.
            Assert.Contains("<style>", html);
            Assert.Contains(".proposal-head", html);
            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("https://", html);
            Assert.Contains("class=\"print-toolbar no-print\"", html); // hidden in print
            Assert.Contains("default-src 'none'", html);
            return html;
        }

        var proposal = await Download($"/Estimates/Proposal/{versionId}?handler=Pdf", @"^EST-\d{4}-\d+-v1\.pdf$");
        Assert.Contains("Penny Dee-Eff", proposal);
        Assert.Contains("Install &lt;new&gt; shingles &amp; ridge cap.", proposal); // still encoded
        Assert.Matches(@"proposal EST-\d{4}-\d+ v1$", fx.Pdf.LastFooter!);

        Assert.Contains("Replace rotted decking", await Download($"/Projects/ChangeOrder/{coId}?handler=Pdf", @"^P-\d{4}-\d+-change-order-1\.pdf$"));
        Assert.Contains("Material order", await Download($"/Projects/OrderSheet/{tradeId}?handler=Pdf", @"^P-\d{4}-\d+-General-order\.pdf$"));
        Assert.Contains("Dumpster in driveway", await Download($"/WorkOrders/Print/{woId}?handler=Pdf", @"\.pdf$"));

        // Email the proposal: the PDF is saved on the estimate and attached, sent to the customer with a CC.
        var proposalPage = await web.GetStringAsync($"/Estimates/Proposal/{versionId}");
        Assert.Contains($"value=\"{contactId}|penny@example.com\"", proposalPage);
        Assert.Contains("Your proposal from", proposalPage);
        string PageToken(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var emailed = await web.PostAsync($"/Estimates/Proposal/{versionId}?handler=Email", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Mail.To"] = $"{contactId}|penny@example.com", ["Mail.Cc"] = "adjuster@example.com",
            ["Mail.Subject"] = "Your proposal", ["Mail.Body"] = "Hi Penny, it's attached.", ["__RequestVerificationToken"] = PageToken(proposalPage),
        }));
        Assert.Equal(HttpStatusCode.Redirect, emailed.StatusCode);
        Assert.Equal($"/Estimates/Proposal/{versionId}", emailed.Headers.Location?.OriginalString);
        Assert.Contains("Proposal emailed to penny@example.com", await web.GetStringAsync($"/Estimates/Proposal/{versionId}"));
        Assert.DoesNotContain("data-proposal-email", fx.Pdf.LastHtml!); // the form isn't in the PDF
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            var saved = await db.Documents.SingleAsync(d => d.EntityId == estimateId && d.CategoryKey == "signed_proposal"); // the version is signed
            Assert.Matches(@"^EST-\d{4}-\d+-v1-signed\.pdf$", saved.FileName);
            Assert.Equal(saved.Id, (await db.EstimateVersions.SingleAsync(v => v.Id == versionId)).SignedPdfDocumentId);
            var mail = await db.EmailMessages.Include(m => m.Attachments).Include(m => m.Recipients).SingleAsync(m => m.Subject == "Your proposal");
            Assert.Equal(saved.Id, mail.Attachments.Single().DocumentId);
            Assert.Contains(mail.Recipients, r => r.Kind == "cc" && r.Address == "adjuster@example.com");
            Assert.True(await db.ActivityEvents.AnyAsync(a => a.EntityId == estimateId && a.EventType == "estimate.emailed"));
        }
        var noRecipient = await web.PostAsync($"/Estimates/Proposal/{versionId}?handler=Email", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Mail.To"] = "", ["Mail.Subject"] = "x", ["Mail.Body"] = "x", ["__RequestVerificationToken"] = PageToken(proposalPage),
        }));
        Assert.Equal(HttpStatusCode.Redirect, noRecipient.StatusCode);
        Assert.Contains("Pick who to send it to.", await web.GetStringAsync($"/Estimates/Proposal/{versionId}"));

        // A change order goes to the customer the same way, saved on the project.
        var coPrint = await web.GetStringAsync($"/Projects/ChangeOrder/{coId}?print=true");
        Assert.Contains("Email to the customer", coPrint);
        Assert.Contains($"value=\"{contactId}|penny@example.com\"", coPrint);
        var coSent = await web.PostAsync($"/Projects/ChangeOrder/{coId}?handler=Email", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Mail.To"] = $"{contactId}|penny@example.com", ["Mail.Subject"] = "Change order 1", ["Mail.Body"] = "Please review.", ["__RequestVerificationToken"] = PageToken(coPrint),
        }));
        Assert.Equal($"/Projects/ChangeOrder/{coId}?print=true", coSent.Headers.Location?.OriginalString);
        Assert.Contains("Change order emailed to penny@example.com", await web.GetStringAsync($"/Projects/ChangeOrder/{coId}?print=true"));

        // An order sheet goes to a typed supplier address, and can be marked ordered at the same time.
        var sheetPrint = await web.GetStringAsync($"/Projects/OrderSheet/{tradeId}?print=true");
        Assert.Contains("Email to the supplier", sheetPrint);
        Assert.Contains("Mark as ordered from", sheetPrint);
        async Task<string> EmailOrder(Dictionary<string, string> form)
        {
            form["__RequestVerificationToken"] = PageToken(sheetPrint);
            await web.PostAsync($"/Projects/OrderSheet/{tradeId}?handler=Email", new FormUrlEncodedContent(form));
            return await web.GetStringAsync($"/Projects/OrderSheet/{tradeId}?print=true");
        }
        Assert.Contains("The order sheet is empty", await EmailOrder(new() { ["Mail.To"] = "orders@supplier.example", ["Mail.Subject"] = "x", ["Mail.Body"] = "x" }));
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<OrderSheetService>().AddLineAsync(tradeId, null, "Coil nails", 4, "BX", null);
        Assert.Contains("Enter the supplier&#x27;s name", await EmailOrder(new() { ["Mail.To"] = "orders@supplier.example", ["Mail.Subject"] = "x", ["Mail.Body"] = "x", ["Mail.MarkOrdered"] = "true" }));
        Assert.Contains("isn&#x27;t an email address", await EmailOrder(new() { ["Mail.To"] = "not an address", ["Mail.Subject"] = "x", ["Mail.Body"] = "x" }));
        var okPage = await EmailOrder(new()
        {
            ["Mail.To"] = "orders@supplier.example", ["Mail.Cc"] = "pm@example.com", ["Mail.Subject"] = "Material order", ["Mail.Body"] = "Attached.",
            ["Mail.MarkOrdered"] = "true", ["Mail.Supplier"] = "ABC Supply",
        });
        Assert.True(okPage.Contains("Order emailed to orders@supplier.example and marked ordered"), System.Text.RegularExpressions.Regex.Match(okPage, "class=\"alert[^>]*>([^<]*)<").Groups[1].Value);
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            var coDoc = await db.Documents.SingleAsync(d => d.EntityId == projectId && d.CategoryKey == "change_order");
            Assert.Equal(coDoc.Id, (await db.EmailMessages.Include(m => m.Attachments).SingleAsync(m => m.Subject == "Change order 1")).Attachments.Single().DocumentId);
            var order = await db.EmailMessages.Include(m => m.Attachments).Include(m => m.Recipients).SingleAsync(m => m.Subject == "Material order");
            Assert.Equal([("to", "orders@supplier.example", (Guid?)null), ("cc", "pm@example.com", null)], order.Recipients.OrderByDescending(r => r.Kind).Select(r => (r.Kind, r.Address, r.ContactId)));
            Assert.Equal("order_sheet", (await db.Documents.SingleAsync(d => d.Id == order.Attachments.Single().DocumentId)).CategoryKey);
            var trade = await db.ProjectTrades.SingleAsync(x => x.Id == tradeId);
            Assert.Equal(("ABC Supply", true), (trade.OrderSupplier, trade.OrderedAt is not null));
            Assert.True(await db.RecordLinks.AnyAsync(l => l.ItemType == "email" && l.ItemId == order.Id && l.EntityType == "trade" && l.EntityId == tradeId));
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<Oes.Infrastructure.Communication.MessagingService>().SendToAddressAsync(new()
            {
                EntityType = "trade", EntityId = tradeId, To = "a@example.com, b@example.com", Subject = "x", Body = "x",
            })); // one address only
        }

        // Another company's document is a 404, as on the print view.
        var (other, otherAdmin) = await fx.CreateTenantAsync();
        _ = other;
        var outsider = fx.App.Browser();
        await outsider.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = otherAdmin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await outsider.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await outsider.GetAsync("/Account/SelectTenant");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/Estimates/Proposal/{versionId}?handler=Pdf")).StatusCode);

        // No browser on the server: back to the print view with the reason.
        fx.Pdf.Unavailable = true;
        try
        {
            var r = await web.GetAsync($"/Estimates/Proposal/{versionId}?handler=Pdf");
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal($"/Estimates/Proposal/{versionId}", r.Headers.Location?.OriginalString);
            Assert.Contains("no Edge or Chrome browser", await web.GetStringAsync($"/Estimates/Proposal/{versionId}"));
        }
        finally { fx.Pdf.Unavailable = false; }

        // The real renderer (the installed Edge or Chrome) turns the captured proposal into a PDF, offline.
        await using var real = new PlaywrightPdfRenderer(Options.Create(new PdfOptions()), NullLogger<PlaywrightPdfRenderer>.Instance);
        var pdf = await real.RenderAsync(proposal + "<img src=\"https://example.invalid/tracker.png\">", "Footer text");
        if (Environment.GetEnvironmentVariable("OES_PDF_OUT") is { Length: > 0 } saveTo) await File.WriteAllBytesAsync(saveTo, pdf); // to look at it
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.True(pdf.Length > 5_000, $"PDF is only {pdf.Length} bytes");
    }

    [Fact]
    public async Task Issuing_and_signing_save_a_pdf_of_each_and_emailing_reuses_it()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid estimateId, contactId;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            contactId = await s.Get<ContactService>().CreateAsync(new ContactInput
            {
                FirstName = "Sal", LastName = "Saved", Methods = [new MethodInput(ContactMethodKinds.Email, "sal@example.com", null, true)],
            });
            var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contactId });
            var est = s.Get<EstimateService>();
            estimateId = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
            var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "SQ", Name = "Roof per square", Unit = "SQ", DefaultPrice = 400m });
            await est.AddProductLineAsync(estimateId, (await est.GetAsync(estimateId)).Sections.Single().Section.Id, product, 20);
        }
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");

        // Issue: the proposal page saves the PDF as issued, once.
        var view = await web.GetStringAsync($"/Estimates/View/{estimateId}");
        var issued = await web.PostAsync($"/Estimates/View/{estimateId}?handler=Issue", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(view) }));
        var saveUrl = issued.Headers.Location!.OriginalString;
        Assert.Matches(@"^/Estimates/Proposal/[0-9a-f-]{36}\?save=True$", saveUrl);
        var versionId = Guid.Parse(saveUrl[20..56]);
        var saving = await web.GetAsync(saveUrl);
        Assert.Equal($"/Estimates/Proposal/{versionId}", saving.Headers.Location?.OriginalString);
        Assert.Contains("Estimate issued. This version is now frozen. A PDF copy is saved on the estimate.", await web.GetStringAsync($"/Estimates/Proposal/{versionId}"));
        Guid issuedPdf;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var v = await s.Get<OesDbContext>().EstimateVersions.SingleAsync(x => x.Id == versionId);
            issuedPdf = v.PdfDocumentId!.Value;
            Assert.Equal("proposal", (await s.Get<OesDbContext>().Documents.SingleAsync(d => d.Id == issuedPdf)).CategoryKey);
        }
        fx.Pdf.Reset();
        Assert.Equal($"/Estimates/Proposal/{versionId}", (await web.GetAsync(saveUrl)).Headers.Location?.OriginalString); // already saved
        Assert.Null(fx.Pdf.LastHtml);

        // Emailing uses the saved copy; nothing is rendered again.
        var page = await web.GetStringAsync($"/Estimates/Proposal/{versionId}");
        await web.PostAsync($"/Estimates/Proposal/{versionId}?handler=Email", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Mail.To"] = $"{contactId}|sal@example.com", ["Mail.Subject"] = "Proposal attached", ["Mail.Body"] = "Here it is.", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Null(fx.Pdf.LastHtml);
        await using (var s = fx.ScopeAs(t.Id, admin))
            Assert.Equal(issuedPdf, (await s.Get<OesDbContext>().EmailMessages.Include(m => m.Attachments).SingleAsync(m => m.Subject == "Proposal attached")).Attachments.Single().DocumentId);

        // Signing saves a second copy, with the signature.
        view = await web.GetStringAsync($"/Estimates/View/{estimateId}");
        var signed = await web.PostAsync($"/Estimates/View/{estimateId}?handler=Sign", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["versionId"] = versionId.ToString(), ["signerName"] = "Sal Saved", ["__RequestVerificationToken"] = Token(view),
        }));
        await web.GetAsync(signed.Headers.Location!.OriginalString);
        Assert.Contains("Sal Saved", fx.Pdf.LastHtml);
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var v = await s.Get<OesDbContext>().EstimateVersions.SingleAsync(x => x.Id == versionId);
            Assert.Equal(issuedPdf, v.PdfDocumentId); // the issued copy stays
            var signedDoc = await s.Get<OesDbContext>().Documents.SingleAsync(d => d.Id == v.SignedPdfDocumentId);
            Assert.Equal("signed_proposal", signedDoc.CategoryKey);
            Assert.EndsWith("-v1-signed.pdf", signedDoc.FileName);
        }
        view = await web.GetStringAsync($"/Estimates/View/{estimateId}");
        Assert.Contains("PDF as issued", view);
        Assert.Contains("Signed PDF", view);
    }

    [Fact]
    public async Task Field_app_reads_issues_and_signs_estimates_and_gets_each_pdf()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid estimateId, oppId, product;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var contactId = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Fay", LastName = "Field" });
            oppId = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contactId });
            var est = s.Get<EstimateService>();
            estimateId = await est.CreateAsync(new NewEstimateInput { OpportunityId = oppId, Method = EstimateMethods.LineItem });
            product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "SQ", Name = "Roof per square", Unit = "SQ", DefaultPrice = 400m });
            await est.AddProductLineAsync(estimateId, (await est.GetAsync(estimateId)).Sections.Single().Section.Id, product, 20);
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

        // The opportunity's estimates, then one in full.
        var list = await Json(await api.GetAsync($"/api/v1/estimates?opportunityId={oppId}"));
        Assert.Equal(estimateId, list.EnumerateArray().Single().GetProperty("id").GetGuid());
        var detail = await Json(await api.GetAsync($"/api/v1/estimates/{estimateId}"));
        Assert.Equal(8000m, detail.GetProperty("subtotal").GetDecimal());
        Assert.Equal("Fay Field", detail.GetProperty("contactName").GetString());
        Assert.Equal(20m, detail.GetProperty("sections")[0].GetProperty("lines")[0].GetProperty("qty").GetDecimal());
        Assert.True(detail.GetProperty("canEdit").GetBoolean());
        Assert.Empty(detail.GetProperty("versions").EnumerateArray());

        // Issue, then get the proposal PDF: made and saved once, the same copy after that.
        var versionId = (await Json(await Post($"/api/v1/estimates/{estimateId}/issue", new { }))).GetProperty("versionId").GetGuid();
        fx.Pdf.Reset();
        var copy = (await Json(await api.GetAsync($"/api/v1/estimate-versions/{versionId}/proposal?handler=Copy"))).GetProperty("documentId").GetGuid();
        Assert.Contains("Fay Field", fx.Pdf.LastHtml);
        fx.Pdf.Reset();
        Assert.Equal(copy, (await Json(await api.GetAsync($"/api/v1/estimate-versions/{versionId}/proposal?handler=Copy"))).GetProperty("documentId").GetGuid());
        Assert.Null(fx.Pdf.LastHtml);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(await api.GetByteArrayAsync($"/api/v1/documents/{copy}"), 0, 5));
        Assert.True((await Json(await api.GetAsync($"/api/v1/estimates/{estimateId}"))).GetProperty("versions")[0].GetProperty("hasPdf").GetBoolean());

        // A rule refusal reads as a message; signing makes a second, signed copy and wins the opportunity.
        var unnamed = await Post($"/api/v1/estimate-versions/{versionId}/sign", new { signerName = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unnamed.StatusCode);
        Assert.Contains("Enter who signed.", await unnamed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"/api/v1/estimate-versions/{versionId}/sign", new { signerName = "Fay Field" })).StatusCode);
        var signed = await Json(await api.GetAsync($"/api/v1/estimates/{estimateId}"));
        Assert.Equal("signed", signed.GetProperty("status").GetString());
        Assert.False(signed.GetProperty("versions")[0].GetProperty("hasPdf").GetBoolean()); // the signed copy isn't made yet
        var signedCopy = (await Json(await api.GetAsync($"/api/v1/estimate-versions/{versionId}/proposal?handler=Copy"))).GetProperty("documentId").GetGuid();
        Assert.NotEqual(copy, signedCopy);
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal("signed_proposal", (await db.Documents.SingleAsync(d => d.Id == signedCopy)).CategoryKey);
            Assert.Equal(Outcomes.Won, (await db.Opportunities.SingleAsync(o => o.Id == oppId)).Outcome);
        }

        // No browser on the server: a clear refusal, not a redirect to the web page.
        Guid second;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var est = s.Get<EstimateService>();
            second = await est.CreateAsync(new NewEstimateInput { OpportunityId = oppId, Method = EstimateMethods.LineItem });
            await est.AddProductLineAsync(second, (await est.GetAsync(second)).Sections.Single().Section.Id, product, 2);
        }
        var v2 = (await Json(await Post($"/api/v1/estimates/{second}/issue", new { }))).GetProperty("versionId").GetGuid();
        fx.Pdf.Unavailable = true;
        try
        {
            var refused = await api.GetAsync($"/api/v1/estimate-versions/{v2}/proposal?handler=Copy");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("no Edge or Chrome browser", await refused.Content.ReadAsStringAsync());
        }
        finally { fx.Pdf.Unavailable = false; }

        // Bearer tokens still don't open web pages outside /api.
        Assert.NotEqual(HttpStatusCode.OK, (await api.GetAsync($"/Estimates/Proposal/{versionId}")).StatusCode);
    }

    [Theory]
    [InlineData("E-2026-12-v1", "E-2026-12-v1.pdf")]
    [InlineData("P-2026-3 change order 2", "P-2026-3-change-order-2.pdf")]
    [InlineData("../../etc/passwd", "etc-passwd.pdf")]
    [InlineData("  ", "document.pdf")]
    public void Download_names_are_safe(string name, string expected) => Assert.Equal(expected, Oes.Web.Infrastructure.PdfDocument.FileName(name));
}
