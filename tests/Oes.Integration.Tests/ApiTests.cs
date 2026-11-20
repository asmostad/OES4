using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ApiTests(MySqlFixture fx)
{
    private sealed record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private HttpClient App() => fx.App.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false,
    });

    private static async Task<HttpResponseMessage> TokenAsync(HttpClient c, string email, string password, Guid? tenantId = null) =>
        await c.PostAsJsonAsync("/api/v1/auth/token", new { email, password, tenantId });

    private async Task<HttpClient> SignedInAsync(AppUser user, Guid? tenantId = null)
    {
        var c = App();
        var r = await TokenAsync(c, user.Email!, MySqlFixture.Password, tenantId);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = (await r.Content.ReadFromJsonAsync<Tokens>())!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t.AccessToken);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>());

    [Fact]
    public async Task Token_sign_in_works_on_the_api_only()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var c = App();
        Assert.Equal(HttpStatusCode.Unauthorized, (await TokenAsync(c, admin.Email!, "wrong-password-123")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TokenAsync(c, "nobody@it.test", "whatever-12345")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me")).StatusCode);

        var ok = await TokenAsync(c, admin.Email!, MySqlFixture.Password);
        var tokens = (await ok.Content.ReadFromJsonAsync<Tokens>())!;
        Assert.Equal(1800, tokens.ExpiresIn);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await Json(await c.GetAsync("/api/v1/me"));
        Assert.Equal(t.Id, me.GetProperty("tenantId").GetGuid());

        // A bearer token is not a web sign-in.
        var page = await c.GetAsync("/Contacts");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/Login", page.Headers.Location!.OriginalString);

        // Bearer calls don't need (and can't get) an anti-forgery token.
        var task = await c.PostAsJsonAsync("/api/v1/tasks", new { title = "From the app" });
        Assert.Equal(HttpStatusCode.Created, task.StatusCode);
    }

    [Fact]
    public async Task Users_in_several_companies_choose_one_and_only_their_own()
    {
        var (t1, admin) = await fx.CreateTenantAsync();
        var (t2, _) = await fx.CreateTenantAsync();
        var (t3, _) = await fx.CreateTenantAsync();
        await fx.AddMemberAsync(t2.Id, "Sales Rep", admin);
        var c = App();
        var choose = await TokenAsync(c, admin.Email!, MySqlFixture.Password);
        Assert.Equal(HttpStatusCode.Conflict, choose.StatusCode);
        var body = await Json(choose);
        Assert.Equal("choose_tenant", body.GetProperty("error").GetString());
        Assert.Equal(2, body.GetProperty("tenants").GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, (await TokenAsync(c, admin.Email!, MySqlFixture.Password, t3.Id)).StatusCode);
        var second = await SignedInAsync(admin, t2.Id);
        Assert.Equal(t2.Id, (await Json(await second.GetAsync("/api/v1/me"))).GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_stop_after_sign_out_everywhere_or_removal()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var c = App();
        var first = (await (await TokenAsync(c, rep.Email!, MySqlFixture.Password)).Content.ReadFromJsonAsync<Tokens>())!;
        var refreshed = await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<Tokens>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "garbage" })).StatusCode);

        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/v1/auth/signout-everywhere", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken })).StatusCode);

        // Removed from the company: the current access token stops working at once.
        var again = await SignedInAsync(rep);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/v1/me")).StatusCode);
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<OesDbContext>().Memberships.Where(m => m.UserId == rep.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Disabled));
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Field_app_endpoints_follow_the_same_rules_as_the_web()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid mine, theirs;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var c1 = await s.Get<ContactService>().CreateAsync(new ContactInput
            { FirstName = "App", LastName = "User", OwnerUserId = rep.Id, Methods = [new MethodInput(ContactMethodKinds.Mobile, "(402) 555-0177", null, true)] });
            mine = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = c1, OwnerUserId = rep.Id, ExpectedValue = 12000 });
            var c2 = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Not", LastName = "Yours", OwnerUserId = admin.Id });
            theirs = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = c2, OwnerUserId = admin.Id });
        }
        var c = await SignedInAsync(rep);

        var list = await Json(await c.GetAsync("/api/v1/opportunities"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var detail = await Json(await c.GetAsync($"/api/v1/opportunities/{mine}"));
        Assert.Equal("(402) 555-0177", detail.GetProperty("contactMethods")[0].GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/opportunities/{theirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"/api/v1/records/opportunity/{theirs}/notes", new { body = "sneaky" })).StatusCode);

        var bad = await c.PostAsJsonAsync("/api/v1/tasks", new { title = "" });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode);
        Assert.Equal("What needs doing?", (await Json(bad)).GetProperty("detail").GetString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)); // close enough to Chicago's today for a due date
        var created = await Json(await c.PostAsJsonAsync("/api/v1/tasks",
            new { title = "Measure the roof", dueDate = today.ToString("yyyy-MM-dd"), entityType = EntityTypes.Opportunity, entityId = mine }));
        var taskId = created.GetProperty("id").GetGuid();
        Assert.Contains((await Json(await c.GetAsync("/api/v1/tasks"))).EnumerateArray(), x => x.GetProperty("id").GetGuid() == taskId);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/api/v1/tasks/{taskId}/done", new { done = true })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/api/v1/records/opportunity/{mine}/notes", new { body = "Customer wants a call after 5" })).StatusCode);
        using var photo = new MultipartFormDataContent { { new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "roof.jpg" } };
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync($"/api/v1/records/opportunity/{mine}/photos", photo)).StatusCode);
        using var exe = new MultipartFormDataContent { { new ByteArrayContent([0x4D, 0x5A]) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }, "file", "x.exe" } };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync($"/api/v1/records/opportunity/{mine}/photos", exe)).StatusCode);

        var timeline = (await Json(await c.GetAsync($"/api/v1/records/opportunity/{mine}/timeline"))).EnumerateArray().ToList();
        Assert.Contains(timeline, i => i.GetProperty("text").GetString() == "Customer wants a call after 5");
        Assert.Contains(timeline, i => i.GetProperty("fileName").GetString() == "roof.jpg");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/records/bogus/{mine}/timeline")).StatusCode);

        // Files: listed with their category, downloaded as stored; someone else's file is a 404.
        var files = (await Json(await c.GetAsync($"/api/v1/records/opportunity/{mine}/documents"))).EnumerateArray().ToList();
        var roof = files.Single(f => f.GetProperty("fileName").GetString() == "roof.jpg");
        Assert.Equal(("image/jpeg", "photos", 4L), (roof.GetProperty("contentType").GetString(), roof.GetProperty("category").GetString(), roof.GetProperty("sizeBytes").GetInt64()));
        var download = await c.GetAsync($"/api/v1/documents/{roof.GetProperty("id").GetGuid()}");
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE0], await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("roof.jpg", download.Content.Headers.ContentDisposition?.FileName);
        Guid hiddenFile;
        await using (var s = fx.ScopeAs(t.Id, admin))
            hiddenFile = await s.Get<RecordItems>().UploadAsync(EntityTypes.Opportunity, theirs, new MemoryStream("%PDF-1.7"u8.ToArray()), "theirs.pdf", "application/pdf", "proposal");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/documents/{hiddenFile}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/records/opportunity/{theirs}/documents")).StatusCode);

        // The app's offline queue resends with the same Idempotency-Key: each action happens once.
        async Task<HttpResponseMessage> Send(HttpContent content, string path, string key)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            req.Headers.Add("Idempotency-Key", key);
            return await c.SendAsync(req);
        }
        var noteKey = Guid.NewGuid().ToString("N");
        var first = await Send(JsonContent.Create(new { body = "Queued while offline" }), $"/api/v1/records/opportunity/{mine}/notes", noteKey);
        var again = await Send(JsonContent.Create(new { body = "Queued while offline" }), $"/api/v1/records/opportunity/{mine}/notes", noteKey);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, again.StatusCode));
        Assert.Equal((await Json(first)).GetProperty("id").GetGuid(), (await Json(again)).GetProperty("id").GetGuid());
        Assert.True(again.Headers.Contains("Idempotent-Replayed"));
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        var photoKey = Guid.NewGuid().ToString("N");
        MultipartFormDataContent Photo() => new() { { new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE1]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "queued.jpg" } };
        Assert.Equal(HttpStatusCode.Created, (await Send(Photo(), $"/api/v1/records/opportunity/{mine}/photos", photoKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(Photo(), $"/api/v1/records/opportunity/{mine}/photos", photoKey)).StatusCode);
        var taskKey = Guid.NewGuid().ToString("N");
        Assert.Equal((HttpStatusCode)422, (await Send(JsonContent.Create(new { title = "" }), "/api/v1/tasks", taskKey)).StatusCode); // a failure isn't kept…
        Assert.Equal(HttpStatusCode.Created, (await Send(JsonContent.Create(new { title = "Queued task" }), "/api/v1/tasks", taskKey)).StatusCode); // …so the fixed retry runs
        Assert.Equal(HttpStatusCode.Created, (await Send(JsonContent.Create(new { title = "Queued task" }), "/api/v1/tasks", taskKey)).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await Send(JsonContent.Create(new { body = "x" }), $"/api/v1/records/opportunity/{mine}/notes", taskKey)).StatusCode); // same key, other request
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(JsonContent.Create(new { body = "x" }), $"/api/v1/records/opportunity/{mine}/notes", "bad key!")).StatusCode);
        await using (var s = fx.ScopeAs(t.Id, rep))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(1, await db.Notes.CountAsync(n => n.Body == "Queued while offline"));
            Assert.Equal(1, await db.Documents.CountAsync(d => d.FileName == "queued.jpg"));
            Assert.Equal(1, await db.Tasks.CountAsync(x => x.Title == "Queued task"));
        }

        var day = await Json(await c.GetAsync("/api/v1/my-day"));
        Assert.True(day.TryGetProperty("appointments", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/appointments?from=2026-01-01&to=2026-12-31")).StatusCode);
    }

    [Fact]
    public async Task Field_app_books_appointments_and_moves_stages()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid mine;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var c = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Book", LastName = "Me", OwnerUserId = rep.Id });
            mine = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = c, OwnerUserId = rep.Id });
        }
        var app = await SignedInAsync(rep);

        var options = await Json(await app.GetAsync("/api/v1/options"));
        var stages = options.GetProperty("stages").EnumerateArray().ToList();
        Assert.Contains(stages, x => x.GetProperty("outcome").GetString() == "won");
        Assert.NotEmpty(options.GetProperty("lostReasons").EnumerateArray());
        Assert.NotEmpty(options.GetProperty("leadSources").EnumerateArray());
        Assert.True(options.TryGetProperty("appointmentTypes", out _));

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        HttpRequestMessage Book() => new(HttpMethod.Post, "/api/v1/appointments")
        {
            Content = JsonContent.Create(new { title = "Roof inspection", typeKey = "inspection", date = date.ToString("yyyy-MM-dd"), startTime = "10:30:00",
                durationMinutes = 90, entityType = EntityTypes.Opportunity, entityId = mine }),
            Headers = { { "Idempotency-Key", "book-" + mine.ToString("N") } },
        };
        Assert.Equal(HttpStatusCode.Created, (await app.SendAsync(Book())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.SendAsync(Book())).StatusCode); // resent by the outbox: still one
        var week = (await Json(await app.GetAsync($"/api/v1/appointments?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"))).EnumerateArray().ToList();
        var booked = Assert.Single(week, a => a.GetProperty("title").GetString() == "Roof inspection");
        Assert.StartsWith($"{date:yyyy-MM-dd}T10:30", booked.GetProperty("startLocal").GetString());
        Assert.Equal((HttpStatusCode)422, (await app.PostAsJsonAsync("/api/v1/appointments", new { title = "", date = date.ToString("yyyy-MM-dd") })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsJsonAsync($"/api/v1/opportunities/{mine}/stage", new { stageId = stages[1].GetProperty("id").GetGuid() })).StatusCode);
        Assert.Equal(stages[1].GetProperty("name").GetString(), (await Json(await app.GetAsync($"/api/v1/opportunities/{mine}"))).GetProperty("stageName").GetString());
        var lost = stages.First(x => x.GetProperty("outcome").GetString() == "lost").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PutAsJsonAsync($"/api/v1/opportunities/{mine}/stage", new { stageId = lost })).StatusCode); // lost needs a reason
    }

    [Fact]
    public async Task Field_app_takes_new_leads_in_one_step()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var app = await SignedInAsync(rep);
        var street = $"{Random.Shared.Next(100, 9999)} Knock St";
        object Lead(string first) => new { firstName = first, lastName = "Doorstep", phone = "(402) 555-0188", email = $"{first.ToLowerInvariant()}@example.com",
            line1 = street, city = "Lincoln", state = "NE", postalCode = "68502", isInsurance = true, notes = "Hail damage on the north slope" };

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/leads") { Content = JsonContent.Create(Lead("Dana")) };
        first.Headers.Add("Idempotency-Key", "lead-" + Guid.NewGuid().ToString("N"));
        var created = await app.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var lead = await Json(created);
        var oppId = lead.GetProperty("opportunityId").GetGuid();

        var detail = await Json(await app.GetAsync($"/api/v1/opportunities/{oppId}"));
        Assert.Equal("Dana Doorstep", detail.GetProperty("contactName").GetString());
        Assert.StartsWith(street, detail.GetProperty("address").GetString());
        Assert.Equal(2, detail.GetProperty("contactMethods").GetArrayLength());
        Assert.True(detail.GetProperty("canEdit").GetBoolean());

        // The neighbour's spouse at the same house: the property is reused.
        var second = await Json(await app.PostAsJsonAsync("/api/v1/leads", Lead("Sam")));
        Assert.Equal(lead.GetProperty("propertyId").GetGuid(), second.GetProperty("propertyId").GetGuid());

        // Search finds them; the contact and the house show their leads and links.
        var hits = (await Json(await app.GetAsync("/api/v1/search?q=Doorstep"))).EnumerateArray().ToList();
        Assert.Contains(hits, h => h.GetProperty("kind").GetString() == "contact" && h.GetProperty("title").GetString() == "Dana Doorstep");
        Assert.Contains(hits, h => h.GetProperty("kind").GetString() == "opportunity" && h.GetProperty("id").GetGuid() == oppId);
        var contact = await Json(await app.GetAsync($"/api/v1/contacts/{lead.GetProperty("contactId").GetGuid()}"));
        Assert.Equal(2, contact.GetProperty("methods").GetArrayLength());
        Assert.Equal(oppId, contact.GetProperty("opportunities")[0].GetProperty("id").GetGuid());
        Assert.Equal(lead.GetProperty("propertyId").GetGuid(), contact.GetProperty("properties")[0].GetProperty("id").GetGuid());
        var house = await Json(await app.GetAsync($"/api/v1/properties/{lead.GetProperty("propertyId").GetGuid()}"));
        Assert.StartsWith(street, house.GetProperty("address").GetString());
        Assert.Equal(2, house.GetProperty("opportunities").GetArrayLength());
        Assert.Contains(house.GetProperty("contacts").EnumerateArray(), c => c.GetProperty("name").GetString() == "Sam Doorstep");
        var otherRep = await SignedInAsync(await fx.AddMemberAsync(t.Id, "Sales Rep"));
        Assert.Equal(HttpStatusCode.NotFound, (await otherRep.GetAsync($"/api/v1/contacts/{lead.GetProperty("contactId").GetGuid()}")).StatusCode);

        // An unfinished address is refused before anything is created.
        int contactsBefore;
        await using (var s = fx.ScopeAs(t.Id, admin)) contactsBefore = await s.Get<OesDbContext>().Contacts.CountAsync();
        var bad = await app.PostAsJsonAsync("/api/v1/leads", new { firstName = "Half", line1 = "1 Nowhere Rd" });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode);
        Assert.Contains("city, state and ZIP", (await Json(bad)).GetProperty("detail").GetString());
        Assert.Equal((HttpStatusCode)422, (await app.PostAsJsonAsync("/api/v1/leads", new { phone = "4025550100" })).StatusCode); // no name
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(contactsBefore, await db.Contacts.CountAsync());
            var opp = await db.Opportunities.SingleAsync(o => o.Id == oppId);
            Assert.Equal((rep.Id, "door_knock", true), (opp.OwnerUserId, opp.SourceKey, opp.IsInsurance));
        }
    }

    [Fact]
    public async Task Field_app_reads_and_clears_the_bell()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid adminNote;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var notes = s.Get<Oes.Infrastructure.Work.NotificationService>();
            notes.Notify(rep.Id, "task.assigned", "Call the adjuster", "Due Friday", "/Tasks");
            notes.Notify(rep.Id, "opportunity.stale", "A lead went quiet", null, "/Opportunities");
            await s.Get<OesDbContext>().SaveChangesAsync();
        }
        await using (var s = fx.ScopeAs(t.Id, rep))
        {
            s.Get<Oes.Infrastructure.Work.NotificationService>().Notify(admin.Id, "task.due", "Admin's own", null, "/Tasks");
            await s.Get<OesDbContext>().SaveChangesAsync();
            adminNote = await s.Get<OesDbContext>().Notifications.Where(n => n.UserId == admin.Id).Select(n => n.Id).SingleAsync();
        }

        var app = await SignedInAsync(rep);
        var bell = await Json(await app.GetAsync("/api/v1/notifications"));
        Assert.Equal(2, bell.GetProperty("unread").GetInt32());
        var items = bell.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count); // only the rep's own
        var first = items.Single(i => i.GetProperty("title").GetString() == "Call the adjuster");
        Assert.Equal(("task.assigned", "/Tasks", false), (first.GetProperty("kind").GetString(), first.GetProperty("url").GetString(), first.GetProperty("read").GetBoolean()));

        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync($"/api/v1/notifications/{first.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        Assert.Equal(1, (await Json(await app.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await app.PutAsync($"/api/v1/notifications/{adminNote}/read", null)).StatusCode); // someone else's

        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        bell = await Json(await app.GetAsync("/api/v1/notifications"));
        Assert.Equal(0, bell.GetProperty("unread").GetInt32());
        Assert.All(bell.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("read").GetBoolean()));
        await using (var s = fx.ScopeAs(t.Id, admin))
            Assert.Null(await s.Get<OesDbContext>().Notifications.Where(n => n.Id == adminNote).Select(n => n.ReadAt).SingleAsync()); // untouched
    }
}
