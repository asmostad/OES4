using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Integration.Tests;

/// <summary>
/// The tenant-isolation suite (architecture §3). Every tenant-owned table must be invisible
/// and unwritable from another tenant. New tenant-owned entities are covered automatically
/// by the model check; add a data test when a table has special rules.
/// </summary>
[Collection(DbCollection.Name)]
public sealed class TenantIsolationTests(MySqlFixture fx)
{
    [Fact]
    public async Task Every_tenant_owned_entity_has_the_tenant_filter()
    {
        await using var scope = fx.ScopeFor(null);
        var db = scope.Get<OesDbContext>();
        var owned = db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList();

        Assert.NotEmpty(owned);
        var missing = owned.Where(t => t.FindDeclaredQueryFilter(OesDbContext.TenantFilter) is null).Select(t => t.ClrType.Name).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Reads_only_see_the_current_tenant()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();

        await using var scope = fx.ScopeFor(a.Id);
        var db = scope.Get<OesDbContext>();

        Assert.All(await db.Roles.ToListAsync(), r => Assert.Equal(a.Id, r.TenantId));
        Assert.All(await db.Memberships.ToListAsync(), m => Assert.Equal(a.Id, m.TenantId));
        Assert.All(await db.RolePermissions.ToListAsync(), p => Assert.Equal(a.Id, p.TenantId));
        Assert.All(await db.MembershipRoles.ToListAsync(), r => Assert.Equal(a.Id, r.TenantId));
        Assert.All(await db.AuditEntries.ToListAsync(), e => Assert.Equal(a.Id, e.TenantId));
        Assert.NotEmpty(await db.Roles.ToListAsync());

        // Looking up B's row by id from A finds nothing.
        var bRoleId = await db.Roles.IgnoreQueryFilters([OesDbContext.TenantFilter]).Where(r => r.TenantId == b.Id).Select(r => r.Id).FirstAsync();
        Assert.Null(await db.Roles.FindAsync(bRoleId));
        Assert.Null(await db.Roles.FirstOrDefaultAsync(r => r.Id == bRoleId));
    }

    [Fact]
    public async Task No_tenant_means_no_rows_and_no_writes()
    {
        await fx.CreateTenantAsync();
        await using var scope = fx.ScopeFor(null);
        var db = scope.Get<OesDbContext>();

        Assert.Empty(await db.Roles.ToListAsync());
        db.Roles.Add(new Role { Name = "Orphan" });
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Inserts_are_stamped_with_the_current_tenant()
    {
        var (a, _) = await fx.CreateTenantAsync();
        await using var scope = fx.ScopeFor(a.Id);
        var db = scope.Get<OesDbContext>();

        var role = new Role { Name = "Estimator" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        Assert.Equal(a.Id, role.TenantId);
        Assert.NotEqual(default, role.CreatedAt);
    }

    [Fact]
    public async Task Cannot_insert_rows_for_another_tenant()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        await using var scope = fx.ScopeFor(a.Id);
        var db = scope.Get<OesDbContext>();

        db.Roles.Add(new Role { Name = "Sneaky", TenantId = b.Id });
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cannot_update_or_delete_another_tenants_rows()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();

        await using (var scope = fx.ScopeFor(a.Id))
        {
            var db = scope.Get<OesDbContext>();
            var bRole = await db.Roles.IgnoreQueryFilters([OesDbContext.TenantFilter]).FirstAsync(r => r.TenantId == b.Id);
            bRole.Name = "Hijacked";
            await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
        }

        await using (var scope = fx.ScopeFor(a.Id))
        {
            var db = scope.Get<OesDbContext>();
            var bRole = await db.Roles.IgnoreQueryFilters([OesDbContext.TenantFilter]).FirstAsync(r => r.TenantId == b.Id);
            db.Roles.Remove(bRole);
            await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
        }

        // An update forged through a detached entity with B's id is refused too.
        await using (var scope = fx.ScopeFor(a.Id))
        {
            var db = scope.Get<OesDbContext>();
            var bRole = await db.Roles.IgnoreQueryFilters([OesDbContext.TenantFilter]).AsNoTracking().FirstAsync(r => r.TenantId == b.Id);
            db.Roles.Update(bRole);
            await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
        }

        await using (var scope = fx.ScopeFor(b.Id))
        {
            var db = scope.Get<OesDbContext>();
            Assert.DoesNotContain(await db.Roles.ToListAsync(), r => r.Name == "Hijacked");
        }
    }

    [Fact]
    public async Task Tenant_of_a_row_can_never_change()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        await using var scope = fx.ScopeFor(a.Id);
        var db = scope.Get<OesDbContext>();

        var role = await db.Roles.FirstAsync();
        role.TenantId = b.Id;
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Bulk_updates_and_deletes_are_filtered_too()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        await using (var scope = fx.ScopeFor(a.Id))
        {
            var db = scope.Get<OesDbContext>();
            await db.Roles.ExecuteUpdateAsync(s => s.SetProperty(r => r.Description, "touched by A"));
        }
        await using (var scope = fx.ScopeFor(b.Id))
        {
            var db = scope.Get<OesDbContext>();
            Assert.DoesNotContain(await db.Roles.ToListAsync(), r => r.Description == "touched by A");
        }
    }

    [Fact]
    public async Task Display_numbers_are_sequential_per_tenant_and_unique_under_concurrency()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        var year = DateTime.UtcNow.Year;

        async Task<string> Next(Guid tenantId)
        {
            await using var scope = fx.ScopeFor(tenantId);
            return await scope.Get<INumberSequence>().NextAsync(NumberedKinds.Project);
        }

        Assert.Equal($"P-{year}-0001", await Next(a.Id));
        Assert.Equal($"P-{year}-0002", await Next(a.Id));
        Assert.Equal($"P-{year}-0001", await Next(b.Id));

        var parallel = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Next(a.Id)));
        Assert.Equal(25, parallel.Distinct().Count());
        Assert.Equal($"P-{year}-0027", parallel.Max());
    }

    [Fact]
    public async Task Platform_scope_is_required_for_cross_tenant_writes()
    {
        var (a, _) = await fx.CreateTenantAsync();
        var (b, _) = await fx.CreateTenantAsync();
        await using var scope = fx.ScopeFor(a.Id);
        var ctx = scope.Get<TenantContext>();
        var db = scope.Get<OesDbContext>();

        using (ctx.BeginPlatformScope())
        {
            db.AuditEntries.Add(new AuditEntry { TenantId = b.Id, Action = "platform.test" });
            await db.SaveChangesAsync();
        }

        db.AuditEntries.Add(new AuditEntry { TenantId = b.Id, Action = "platform.test" });
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }
}
