using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Oes.Infrastructure.Identity;

namespace Oes.Integration.Tests;

/// <summary>End-to-end checks through the real middleware: sign-in, tenant choice, permissions, API.</summary>
[Collection(DbCollection.Name)]
public sealed partial class WebSecurityTests(MySqlFixture fx)
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static async Task<string> TokenFrom(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostForm(HttpClient client, string url, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenFrom(client, url);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task SignIn(HttpClient client, AppUser user)
    {
        var res = await PostForm(client, "/Account/Login", new() { ["Email"] = user.Email!, ["Password"] = MySqlFixture.Password });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/Account/SelectTenant", res.Headers.Location!.OriginalString);
    }

    public sealed record Me(Guid UserId, Guid TenantId, string TenantName, List<string> Permissions);

    [Fact]
    public async Task Anonymous_users_are_sent_to_login_and_the_api_says_401()
    {
        var client = fx.App.Browser();
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/Login", page.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var (_, admin) = await fx.CreateTenantAsync();
        var client = fx.App.Browser();
        var res = await PostForm(client, "/Account/Login", new() { ["Email"] = admin.Email!, ["Password"] = "not-the-password" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Invalid email or password", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Single_tenant_user_lands_in_their_tenant()
    {
        var (tenant, admin) = await fx.CreateTenantAsync();
        var client = fx.App.Browser();
        await SignIn(client, admin);

        var pick = await client.GetAsync("/Account/SelectTenant");
        Assert.Equal(HttpStatusCode.Redirect, pick.StatusCode);
        Assert.Equal("/", pick.Headers.Location!.OriginalString);

        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");
        Assert.Equal(tenant.Id, me!.TenantId);
        Assert.Contains("users.manage", me.Permissions);

        var home = await client.GetStringAsync("/");
        Assert.Contains(tenant.Name, home);
    }

    [Fact]
    public async Task Multi_tenant_user_picks_and_cannot_enter_a_tenant_they_dont_belong_to()
    {
        var (a, admin) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        var (c, _) = await fx.CreateTenantAsync();
        await fx.AddMemberAsync(b.Id, "Sales Rep", admin);

        var client = fx.App.Browser();
        await SignIn(client, admin);

        var list = await client.GetAsync("/Account/SelectTenant");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var html = await list.Content.ReadAsStringAsync();
        Assert.Contains(a.Name, html);
        Assert.Contains(b.Name, html);
        Assert.DoesNotContain(c.Name, html);

        // Not a member of C: refused.
        var denied = await PostForm(client, "/Account/SelectTenant", new() { ["tenantId"] = c.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("AccessDenied", denied.Headers.Location!.OriginalString);

        // Member of B as a Sales Rep: allowed, with B's permissions only.
        var ok = await PostForm(client, "/Account/SelectTenant", new() { ["tenantId"] = b.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");
        Assert.Equal(b.Id, me!.TenantId);
        Assert.DoesNotContain("users.manage", me.Permissions);

        // ...so B's admin pages are off limits, even though the same user administers A.
        var users = await client.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Contains("AccessDenied", users.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Admin_pages_list_only_the_current_tenants_users()
    {
        var (a, adminA) = await fx.CreateTenantAsync();
        var (_, adminB) = await fx.CreateTenantAsync();

        var client = fx.App.Browser();
        await SignIn(client, adminA);
        await client.GetAsync("/Account/SelectTenant");

        var html = await client.GetStringAsync("/Admin/Users");
        Assert.Contains(adminA.Email!, html);
        Assert.DoesNotContain(adminB.Email!, html);
        Assert.Contains(a.Name, html);
    }

    [Fact]
    public async Task Disabled_members_are_signed_out_on_their_next_request()
    {
        var (a, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(a.Id, "Sales Rep");

        var client = fx.App.Browser();
        await SignIn(client, rep);
        await client.GetAsync("/Account/SelectTenant");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        await using (var scope = fx.ScopeFor(a.Id))
        {
            var db = scope.Get<Oes.Infrastructure.Persistence.OesDbContext>();
            var m = db.Memberships.Single(x => x.UserId == rep.Id);
            m.Status = Oes.Domain.Tenancy.MembershipStatus.Disabled;
            await db.SaveChangesAsync();
        }

        var after = await client.GetAsync("/api/v1/me");
        Assert.True(after.StatusCode == HttpStatusCode.Unauthorized, $"{after.StatusCode} {after.Headers.Location}");
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected()
    {
        var (_, admin) = await fx.CreateTenantAsync();
        var client = fx.App.Browser();
        await SignIn(client, admin);
        await client.GetAsync("/Account/SelectTenant");

        var res = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_sent()
    {
        var res = await fx.App.Browser().GetAsync("/Account/Login");
        Assert.True(res.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
