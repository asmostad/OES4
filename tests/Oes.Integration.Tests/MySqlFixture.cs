using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;
using Testcontainers.MySql;

namespace Oes.Integration.Tests;

/// <summary>
/// One real MySQL (Docker) per test run, migrated once. Tests create their own tenants with
/// unique names, so they don't depend on each other's data.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    public const string Image = "mysql:9.6";

    private readonly MySqlContainer _mysql = new MySqlBuilder(Image)
        .WithDatabase("oes4_test")
        .WithUsername("root")
        .WithPassword("oes4test")
        .Build();

    public string ConnectionString { get; private set; } = "";
    public TestApp App { get; private set; } = null!;
    public string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), "oes4-it-" + Guid.NewGuid().ToString("N"));

    /// <summary>The app's clock: real time unless a test pins it (always unpin in a finally).</summary>
    public TestClock Clock { get; } = new();

    /// <summary>The model behind the assistant: scripted per test, never the real API.</summary>
    public ScriptedAiClient Ai { get; } = new();

    /// <summary>Calendar feeds by URL: tests never reach the internet.</summary>
    public FakeFetcher Feeds { get; } = new();

    /// <summary>PDFs: captures the HTML instead of starting a browser (PdfTests runs the real renderer once).</summary>
    public CapturingPdfRenderer Pdf { get; } = new();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        ConnectionString = _mysql.GetConnectionString() + ";AllowPublicKeyRetrieval=true;SslMode=Disabled";
        App = new TestApp(this);
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OesDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await _mysql.DisposeAsync();
        if (Directory.Exists(FilesRoot)) Directory.Delete(FilesRoot, true);
    }

    /// <summary>A DI scope acting as the given tenant (like one request or job).</summary>
    public AsyncServiceScope ScopeFor(Guid? tenantId)
    {
        var scope = App.Services.CreateAsyncScope();
        // IHttpContextAccessor is AsyncLocal: clear any user an earlier ScopeAs left in this async flow.
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = null;
        if (tenantId is { } id) scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(id);
        return scope;
    }

    /// <summary>A scope acting as a signed-in user in a tenant, so services see their permissions.</summary>
    public AsyncServiceScope ScopeAs(Guid tenantId, AppUser user)
    {
        var scope = ScopeFor(tenantId);
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())], "test");
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
            new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity), RequestServices = scope.ServiceProvider };
        return scope;
    }

    public const string Password = "Integration-Test-Pass-1";

    /// <summary>Creates a user and a tenant they administer.</summary>
    public async Task<(Tenant Tenant, AppUser Admin)> CreateTenantAsync(string? name = null)
    {
        name ??= "Tenant " + Guid.NewGuid().ToString("N")[..8];
        await using var scope = App.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"admin-{Guid.NewGuid():N}@it.test";
        var admin = new AppUser { UserName = email, Email = email, FirstName = "Admin", LastName = name };
        var result = await users.CreateAsync(admin, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        var tenant = await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().ProvisionAsync(
            new ProvisionTenantRequest(name, name.ToLowerInvariant().Replace(' ', '-'), "America/Chicago", admin));
        return (tenant, admin);
    }

    /// <summary>Adds a user to a tenant with the named role (e.g. "Sales Rep").</summary>
    public async Task<AppUser> AddMemberAsync(Guid tenantId, string roleName, AppUser? existing = null)
    {
        await using var scope = ScopeFor(tenantId);
        var sp = scope.ServiceProvider;
        var user = existing;
        if (user is null)
        {
            var email = $"user-{Guid.NewGuid():N}@it.test";
            user = new AppUser { UserName = email, Email = email, FirstName = "Member", LastName = roleName };
            var result = await sp.GetRequiredService<UserManager<AppUser>>().CreateAsync(user, Password);
            Assert.True(result.Succeeded);
        }
        var db = sp.GetRequiredService<OesDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);
        var m = new TenantMembership { UserId = user.Id };
        m.Roles.Add(new MembershipRole { RoleId = role.Id });
        db.Memberships.Add(m);
        await db.SaveChangesAsync();
        return user;
    }
}

[CollectionDefinition(Name)]
public sealed class DbCollection : ICollectionFixture<MySqlFixture>
{
    public const string Name = "mysql";
}

public sealed class TestApp(MySqlFixture fx) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so values are visible to code that reads
        // configuration while services are being registered in Program.cs.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ConnectionStrings:Oes"] = fx.ConnectionString,
            ["App:BaseUrl"] = "https://localhost",
            ["FileStore:RootPath"] = fx.FilesRoot,
            ["Messaging:MailProvider"] = "Dev",
            ["Messaging:SmsProvider"] = "Dev",
            ["Messaging:PickupDirectory"] = Path.Combine(fx.FilesRoot, "messages"),
            ["Messaging:DevWebhookSecret"] = "test-webhook-secret",
            ["SystemMail:Provider"] = "Pickup",
            ["SystemMail:PickupDirectory"] = Path.Combine(fx.FilesRoot, "mail"),
            ["DataProtection:KeysPath"] = "",
            ["RateLimits:LoginPerMinute"] = "10000",
            ["Serilog:WriteTo:1:Args:path"] = Path.Combine(fx.FilesRoot, "logs", "web-.log"),
            ["Serilog:MinimumLevel:Default"] = "Warning",
        })
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IClock>(); s.AddSingleton<IClock>(fx.Clock);
            s.RemoveAll<IAiClient>(); s.AddSingleton<IAiClient>(fx.Ai);
            s.RemoveAll<Oes.Infrastructure.Http.ISafeFetcher>(); s.AddSingleton<Oes.Infrastructure.Http.ISafeFetcher>(fx.Feeds);
            s.RemoveAll<IPdfRenderer>(); s.AddSingleton<IPdfRenderer>(fx.Pdf);
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });
}

internal static class ScopeExtensions
{
    public static T Get<T>(this AsyncServiceScope s) where T : notnull => s.ServiceProvider.GetRequiredService<T>();
    public static ITenantContext Tenant(this AsyncServiceScope s) => s.Get<ITenantContext>();
}

/// <summary>Real time, or a pinned instant for tests about schedules and quiet hours.</summary>
public sealed class TestClock : IClock
{
    public DateTime? Pinned { get; set; }
    public DateTime UtcNow => Pinned is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : DateTime.UtcNow;
}

/// <summary>Serves registered URLs (after the real URL checks); anything else fails like an unreachable site.</summary>
public sealed class FakeFetcher : Oes.Infrastructure.Http.ISafeFetcher
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _content = new();

    public void Set(string url, string content) => _content[Oes.Infrastructure.Http.SafeHttpFetcher.Normalize(url).ToString()] = content;

    public Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct = default)
    {
        var key = Oes.Infrastructure.Http.SafeHttpFetcher.Normalize(url).ToString();
        return _content.TryGetValue(key, out var c) ? Task.FromResult(c) : throw new Oes.Infrastructure.Http.SafeFetchException("Couldn't reach the calendar site.");
    }
}

/// <summary>Records what it was asked to render and returns a stand-in PDF; can pretend no browser is installed.</summary>
public sealed class CapturingPdfRenderer : IPdfRenderer
{
    public string? LastHtml { get; private set; }
    public string? LastFooter { get; private set; }
    public bool Unavailable { get; set; }

    public Task<byte[]> RenderAsync(string html, string? footer = null, CancellationToken ct = default)
    {
        if (Unavailable) throw new PdfUnavailableException("PDFs can't be made on this server: no Edge or Chrome browser was found.");
        (LastHtml, LastFooter) = (html, footer);
        return Task.FromResult("%PDF-1.7 stand-in"u8.ToArray());
    }
}
