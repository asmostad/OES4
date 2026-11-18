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

        var day = await Json(await c.GetAsync("/api/v1/my-day"));
        Assert.True(day.TryGetProperty("appointments", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/appointments?from=2026-01-01&to=2026-12-31")).StatusCode);
    }
}
