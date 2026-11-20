using System.Net;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using static Oes.Integration.Tests.ScriptedAiClient;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class KnowledgeTests(MySqlFixture fx)
{
    // Made-up company policies.
    private const string Warranty = """
        # Workmanship warranty

        We warrant our installation labor for 10 years from the completion date. It transfers once, to the next owner.

        # Making a claim

        Customers call the office; we inspect within 5 business days.
        """;

    [Fact]
    public async Task Admins_write_articles_everyone_with_the_assistant_searches_them()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid warrantyId;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var kb = s.Get<KnowledgeService>();
            await Assert.ThrowsAsync<CrmRuleException>(() => kb.SaveAsync(null, new KnowledgeInput { Title = "", Body = "x" }));
            await Assert.ThrowsAsync<CrmRuleException>(() => kb.SaveAsync(null, new KnowledgeInput { Title = "Empty", Body = "  " }));
            warrantyId = await kb.SaveAsync(null, new KnowledgeInput { Title = "Workmanship warranty", Category = "Warranties", Body = Warranty });
            await kb.SaveAsync(null, new KnowledgeInput { Title = "Gutter products", Category = "Products", Body = "We install 6 inch seamless gutters. Labor is quoted per foot." });
            await kb.SaveAsync(null, new KnowledgeInput { Title = "Old warranty", Body = "Workmanship warranty was 5 years before 2020.", IsActive = false });

            var rows = await kb.ListAsync();
            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows.Single(r => r.Article.Id == warrantyId).Passages);
            Assert.Equal(["Products", "Warranties"], await kb.CategoriesAsync());
        }

        // A rep can search (active articles only) but not edit.
        await using (var s = fx.ScopeAs(t.Id, rep))
        {
            var kb = s.Get<KnowledgeService>();
            var hits = await kb.SearchAsync("How long is the workmanship warranty?");
            Assert.Equal(("Workmanship warranty", "Workmanship warranty"), (hits[0].Title, hits[0].Heading));
            Assert.Contains("10 years", hits[0].Text);
            Assert.DoesNotContain(hits, h => h.Title == "Old warranty");
            Assert.Equal("Gutter products", (await kb.SearchAsync("gutter sizes")).Single().Title);
            Assert.Empty(await kb.SearchAsync("the and of"));
            await Assert.ThrowsAsync<RecordForbiddenException>(() => kb.SaveAsync(null, new KnowledgeInput { Title = "Mine", Body = "x" }));
            await Assert.ThrowsAsync<RecordForbiddenException>(() => kb.ListAsync());
        }

        // Editing rebuilds the passages; another company sees none of it.
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var kb = s.Get<KnowledgeService>();
            await kb.SaveAsync(warrantyId, new KnowledgeInput { Title = "Workmanship warranty", Category = "Warranties", Body = "Labor is warranted for 12 years." });
            Assert.Contains("12 years", (await kb.SearchAsync("workmanship warranty")).First().Text);
            Assert.Equal(1, await s.Get<OesDbContext>().KnowledgeChunks.CountAsync(c => c.ArticleId == warrantyId));
        }
        var (other, otherAdmin) = await fx.CreateTenantAsync();
        await using (var o = fx.ScopeAs(other.Id, otherAdmin))
        {
            Assert.Empty(await o.Get<KnowledgeService>().SearchAsync("workmanship warranty"));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => o.Get<KnowledgeService>().GetAsync(warrantyId));
        }
    }

    [Fact]
    public async Task The_assistant_searches_the_knowledge_base_and_cites_it()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<KnowledgeService>().SaveAsync(null, new KnowledgeInput { Title = "Workmanship warranty", Body = Warranty });

        fx.Ai.Reset(
            Calls("search_knowledge", new { query = "workmanship warranty transfer" }),
            Calls("search_knowledge", new { query = "drone inspections" }),
            Says("10 years, and it transfers once (Workmanship warranty)."));
        await using (var s = fx.ScopeAs(t.Id, rep))
        {
            var assistant = s.Get<AssistantService>();
            var convo = await assistant.StartAsync(null, null);
            Assert.StartsWith("10 years", await assistant.SendAsync(convo, "Does our warranty transfer?"));
        }
        Assert.Contains(fx.Ai.Requests[0].Tools, d => d.Name == "search_knowledge");
        Assert.Contains("search_knowledge", fx.Ai.Requests[0].System);
        var found = fx.Ai.Requests[1].Messages[^1].Blocks.OfType<AiToolResult>().Single();
        Assert.False(found.IsError);
        Assert.Contains("\"article\":\"Workmanship warranty\"", found.Content);
        Assert.Contains("transfers once", found.Content);
        Assert.Contains("Nothing in the knowledge base matches", fx.Ai.Requests[2].Messages[^1].Blocks.OfType<AiToolResult>().Single().Content);
    }

    [Fact]
    public async Task The_admin_pages_add_test_and_delete_articles()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        Assert.Contains("href=\"/Admin/Knowledge\"", await web.GetStringAsync("/Admin/Settings")); // in the menu

        // Fill an article from a Markdown file; the title comes from the file name.
        var form = await web.GetStringAsync("/Admin/KnowledgeEdit");
        var upload = new MultipartFormDataContent
        {
            { new StringContent(Token(form)), "__RequestVerificationToken" },
            { new StringContent(""), "Input.Title" },
            { new StringContent("Procedures"), "Input.Category" },
            { new StringContent(""), "Input.Body" },
            { new StringContent("true"), "Input.IsActive" },
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(Warranty)), "File", "Warranty terms.md" },
        };
        var saved = await web.PostAsync("/Admin/KnowledgeEdit", upload);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var list = await web.GetStringAsync("/Admin/Knowledge?q=claim+inspection");
        Assert.Contains("Warranty terms", list);
        Assert.Contains("Making a claim", list); // the passage found, under its heading
        Assert.Contains("data-hit", list);

        Guid id;
        await using (var s = fx.ScopeAs(t.Id, admin)) id = (await s.Get<KnowledgeService>().ListAsync()).Single().Article.Id;
        var edit = await web.GetStringAsync($"/Admin/KnowledgeEdit/{id}");
        Assert.Contains("we inspect within 5 business days", edit);
        var bad = await web.PostAsync($"/Admin/KnowledgeEdit/{id}", new MultipartFormDataContent
        {
            { new StringContent(Token(edit)), "__RequestVerificationToken" },
            { new StringContent("x"), "Input.Title" },
            { new ByteArrayContent([1, 2, 3]), "File", "scan.pdf" },
        });
        Assert.Contains("couldn&#x27;t be read as a PDF", await bad.Content.ReadAsStringAsync());

        // A real PDF with text becomes an article the assistant can search.
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var pdfPage = builder.AddPage(UglyToad.PdfPig.Content.PageSize.Letter);
        pdfPage.AddText("Gutter guard care", 14, new UglyToad.PdfPig.Core.PdfPoint(72, 720), font);
        pdfPage.AddText("Rinse the guards with a hose every spring.", 12, new UglyToad.PdfPig.Core.PdfPoint(72, 700), font);
        var newForm = await web.GetStringAsync("/Admin/KnowledgeEdit");
        var pdfSaved = await web.PostAsync("/Admin/KnowledgeEdit", new MultipartFormDataContent
        {
            { new StringContent(Token(newForm)), "__RequestVerificationToken" },
            { new StringContent("true"), "Input.IsActive" },
            { new ByteArrayContent(builder.Build()), "File", "Gutter guard care.pdf" },
        });
        Assert.Equal(HttpStatusCode.Redirect, pdfSaved.StatusCode);
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var hit = (await s.Get<KnowledgeService>().SearchAsync("rinse gutter guards")).First();
            Assert.Equal("Gutter guard care", hit.Title); // title from the file name
            Assert.Contains("Rinse the guards with a hose every spring.", hit.Text);
            await s.Get<KnowledgeService>().DeleteAsync(hit.ArticleId);
        }

        var deleted = await web.PostAsync($"/Admin/KnowledgeEdit/{id}?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(edit) }));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            Assert.Empty(await s.Get<KnowledgeService>().ListAsync());
            Assert.Equal(0, await s.Get<OesDbContext>().KnowledgeChunks.CountAsync());
            Assert.True(await s.Get<OesDbContext>().AuditEntries.AnyAsync(a => a.Action == "knowledge.deleted"));
        }
    }
}
