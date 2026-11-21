using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Files;

public sealed class FileStoreOptions
{
    public const string Section = "FileStore";

    /// <summary>Private folder outside the web root, e.g. D:\OES4Data\files.</summary>
    public string RootPath { get; set; } = "";

    public long MaxBytes { get; set; } = 50 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".esx", ".xml", ".json", ".eml", ".msg",
    ];
}

public sealed class FileRejectedException(string message) : InvalidOperationException(message);

/// <summary>
/// Stores files on the VPS disk under {root}/{tenant}/{yyyy}/{MM}/{random}{ext}.
/// Keys are random and tenant-prefixed, so a key from one tenant can never be opened
/// from another, and nothing in a key comes from the uploaded file name except a
/// checked extension. Swap for an S3 store later behind the same interface.
/// </summary>
public sealed partial class DiskFileStore(IOptions<FileStoreOptions> options, ITenantContext tenant) : IFileStore
{
    private readonly FileStoreOptions _o = options.Value;

    [GeneratedRegex(@"^[0-9a-f]{32}/\d{4}/\d{2}/[0-9a-f]{32}(\.[a-z0-9]{1,8})?$")]
    private static partial Regex KeyPattern();

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant is set.");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_o.AllowedExtensions.Contains(ext))
            throw new FileRejectedException($"Files of type '{ext}' are not allowed.");

        var now = DateTime.UtcNow;
        var key = $"{tenantId:N}/{now:yyyy}/{now:MM}/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}{ext}";
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        long written = 0;
        var buffer = new byte[81920];
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                written += read;
                if (written > _o.MaxBytes)
                    throw new FileRejectedException($"File is larger than the {_o.MaxBytes / 1024 / 1024} MB limit.");
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        return new StoredFile(key, Path.GetFileName(fileName), contentType, written);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = CheckedPath(key);
        Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(s);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(CheckedPath(key));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(CheckedPath(key)));

    private string CheckedPath(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new FileRejectedException("Invalid file key.");
        if (!tenant.IsPlatformScope && !key.StartsWith($"{tenant.TenantId:N}/", StringComparison.Ordinal))
            throw new FileRejectedException("File belongs to another tenant.");
        return PathFor(key);
    }

    private string PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(_o.RootPath)) throw new InvalidOperationException("FileStore:RootPath is not configured.");
        var root = Path.GetFullPath(_o.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new FileRejectedException("Invalid file key.");
        return full;
    }
}
