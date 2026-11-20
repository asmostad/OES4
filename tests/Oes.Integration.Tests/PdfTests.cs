using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oes.Domain.Estimating;
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
        Guid versionId, coId, tradeId, woId;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "12 Paper Ln", City = "Lincoln", State = "NE", PostalCode = "68502" });
            var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Penny", LastName = "Dee-Eff" });
            var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property });
            var est = s.Get<EstimateService>();
            var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
            var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
            await est.UpdateSectionAsync(section, "Roof", "- Tear off to the deck.\n- Install <new> shingles & ridge cap.");
            var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "SQ", Name = "Roof per square", Unit = "SQ", DefaultPrice = 400m });
            await est.AddProductLineAsync(id, section, product, 25);
            versionId = await est.IssueAsync(id, null);
            await est.MarkSignedAsync(versionId, "Penny Dee-Eff");

            var projects = s.Get<ProjectService>();
            var projectId = await projects.CreateFromEstimateAsync(id);
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

    [Theory]
    [InlineData("E-2026-12-v1", "E-2026-12-v1.pdf")]
    [InlineData("P-2026-3 change order 2", "P-2026-3-change-order-2.pdf")]
    [InlineData("../../etc/passwd", "etc-passwd.pdf")]
    [InlineData("  ", "document.pdf")]
    public void Download_names_are_safe(string name, string expected) => Assert.Equal(expected, Oes.Web.Infrastructure.PdfDocument.FileName(name));
}
