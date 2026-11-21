using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.UnitTests;

public class DisplayNumberTests
{
    [Theory]
    [InlineData("P", 2026, 311, "P-2026-0311")]
    [InlineData("EST", 2026, 1, "EST-2026-0001")]
    [InlineData("WO", 2027, 12345, "WO-2027-12345")]
    public void Formats_prefix_year_and_padded_counter(string prefix, int year, long value, string expected) =>
        Assert.Equal(expected, DisplayNumber.Format(prefix, year, value));

    [Fact]
    public void Rejects_zero() => Assert.Throws<ArgumentOutOfRangeException>(() => DisplayNumber.Format("P", 2026, 0));
}

public class GuidStorageTests
{
    [Fact]
    public void Round_trips()
    {
        var c = new GuidBinaryConverter();
        var g = Guid.CreateVersion7();
        var bytes = (byte[])c.ConvertToProvider(g)!;
        Assert.Equal(16, bytes.Length);
        Assert.Equal(g, (Guid)c.ConvertFromProvider(bytes)!);
    }

    [Fact]
    public void Stored_bytes_sort_in_creation_order()
    {
        // GUID v7 starts with a millisecond timestamp; big-endian storage must keep that order
        // so inserts append to the index instead of landing randomly.
        var c = new GuidBinaryConverter();
        var t0 = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 50).Select(i => Guid.CreateVersion7(t0.AddMilliseconds(i * 3))).ToList();
        var stored = ids.Select(g => (byte[])c.ConvertToProvider(g)!).ToList();
        var sorted = stored.OrderBy(b => Convert.ToHexString(b), StringComparer.Ordinal).ToList();
        Assert.Equal(stored, sorted);
    }
}

public class PermissionCatalogTests
{
    [Fact]
    public void Keys_are_unique_and_well_formed()
    {
        var keys = Permissions.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Matches(@"^[a-z_]+\.[a-z_]+$", k));
    }

    [Fact]
    public void Every_constant_is_in_the_catalog()
    {
        var constants = typeof(Permissions).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
        Assert.All(constants, k => Assert.True(Permissions.IsKnown(k), $"{k} is not in Permissions.All"));
    }

    [Fact]
    public void Default_roles_use_known_keys_and_administrator_has_all()
    {
        Assert.All(DefaultRoles.All.SelectMany(r => r.PermissionKeys), k => Assert.True(Permissions.IsKnown(k), k));
        var admin = DefaultRoles.All.First();
        Assert.Equal(Permissions.All.Count, admin.PermissionKeys.Distinct().Count());
    }
}

public class NoRoleNameChecksTests
{
    // Design rule: authorization checks permissions, never role names (OES3 hard-coded "admin").
    private static readonly Regex Forbidden = new(@"IsInRole\s*\(|Roles\s*=\s*""|RequireRole\s*\(", RegexOptions.Compiled);

    [Fact]
    public void Source_has_no_role_name_checks()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => Forbidden.IsMatch(File.ReadAllText(f)))
            .ToList();
        Assert.Empty(offenders);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OES4.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}

public sealed class DiskFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oes4-files-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _tenantA = Guid.CreateVersion7();
    private readonly Guid _tenantB = Guid.CreateVersion7();

    private DiskFileStore StoreFor(Guid tenantId)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId);
        return new DiskFileStore(Options.Create(new FileStoreOptions { RootPath = _root, MaxBytes = 1024 }), ctx);
    }

    [Fact]
    public async Task Saves_under_a_random_tenant_prefixed_key_and_reads_back()
    {
        var store = StoreFor(_tenantA);
        var saved = await store.SaveAsync(new MemoryStream("hello"u8.ToArray()), "..\\..\\evil name.pdf", "application/pdf");

        Assert.StartsWith($"{_tenantA:N}/", saved.Key);
        Assert.DoesNotContain("evil", saved.Key);
        Assert.EndsWith(".pdf", saved.Key);
        await using var s = await store.OpenReadAsync(saved.Key);
        Assert.Equal("hello", await new StreamReader(s).ReadToEndAsync());
    }

    [Fact]
    public async Task Another_tenant_cannot_open_the_file()
    {
        var saved = await StoreFor(_tenantA).SaveAsync(new MemoryStream([1, 2, 3]), "a.png", "image/png");
        await Assert.ThrowsAsync<FileRejectedException>(() => StoreFor(_tenantB).OpenReadAsync(saved.Key));
    }

    [Theory]
    [InlineData("../../windows/system32/x.dll")]
    [InlineData("C:/windows/win.ini")]
    [InlineData("")]
    public async Task Rejects_malformed_keys(string key) =>
        await Assert.ThrowsAsync<FileRejectedException>(() => StoreFor(_tenantA).OpenReadAsync(key));

    [Fact]
    public async Task Rejects_disallowed_types_and_oversize_files()
    {
        var store = StoreFor(_tenantA);
        await Assert.ThrowsAsync<FileRejectedException>(() => store.SaveAsync(new MemoryStream([1]), "x.exe", "application/octet-stream"));
        await Assert.ThrowsAsync<FileRejectedException>(() => store.SaveAsync(new MemoryStream(new byte[2048]), "big.pdf", "application/pdf"));
        Assert.Empty(Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories) : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

public class TenantContextTests
{
    [Fact]
    public void Tenant_cannot_be_switched_within_a_scope()
    {
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.CreateVersion7());
        Assert.Throws<InvalidOperationException>(() => ctx.SetTenant(Guid.CreateVersion7()));
    }

    [Fact]
    public void Platform_scope_ends_on_dispose()
    {
        var ctx = new TenantContext();
        using (ctx.BeginPlatformScope()) Assert.True(ctx.IsPlatformScope);
        Assert.False(ctx.IsPlatformScope);
    }
}
