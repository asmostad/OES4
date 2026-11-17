using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Web.Infrastructure;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string? AdminEmail { get; set; }

    /// <summary>Set with `dotnet user-secrets`, never in appsettings.</summary>
    public string? AdminPassword { get; set; }

    public string[] TenantNames { get; set; } = ["Demo Roofing", "Second Demo Co"];
}

/// <summary>
/// Development only: applies migrations, then creates demo tenants and an admin user who
/// belongs to all of them (to exercise the tenant picker). Does nothing once users exist.
/// </summary>
public static class DevSeeder
{
    public static async Task RunAsync(IServiceProvider root, CancellationToken ct = default)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder");
        var db = sp.GetRequiredService<OesDbContext>();

        await db.Database.MigrateAsync(ct);

        var opts = sp.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new();
        if (await db.Users.AnyAsync(ct)) return;
        if (string.IsNullOrWhiteSpace(opts.AdminEmail) || string.IsNullOrWhiteSpace(opts.AdminPassword))
        {
            log.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set (use dotnet user-secrets). Skipping seed.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var admin = new AppUser
        {
            UserName = opts.AdminEmail, Email = opts.AdminEmail, EmailConfirmed = true,
            FirstName = "Demo", LastName = "Admin",
        };
        var created = await users.CreateAsync(admin, opts.AdminPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException("Seed admin not created: " + string.Join("; ", created.Errors.Select(e => e.Description)));

        foreach (var name in opts.TenantNames)
        {
            // Each tenant needs its own scope: a scope's tenant context is set once.
            using var tScope = root.CreateScope();
            var provisioner = tScope.ServiceProvider.GetRequiredService<TenantProvisioner>();
            var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            await provisioner.ProvisionAsync(new ProvisionTenantRequest(name, slug, "America/Chicago", admin), ct);
            log.LogInformation("Seeded tenant {Tenant}", name);
        }
    }
}
