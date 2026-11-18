using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Communication;

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    /// <summary>"Dev" writes to <see cref="PickupDirectory"/>; "None" (the default) refuses to send. Graph and Twilio plug in later.</summary>
    public string MailProvider { get; set; } = "None";
    public string SmsProvider { get; set; } = "None";
    public string PickupDirectory { get; set; } = "message-pickup";

    /// <summary>Shared secret for the development inbound-SMS webhook. Unset = webhook disabled.</summary>
    public string? DevWebhookSecret { get; set; }
}

/// <summary>Development mail provider: writes each message to an .html file instead of sending it.</summary>
public sealed class DevMailProvider(IOptions<MessagingOptions> options, ILogger<DevMailProvider> log) : IMailProvider
{
    public string Name => "dev";

    public async Task<ProviderResult> SendAsync(Guid userId, OutgoingEmail email, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetFullPath(options.Value.PickupDirectory), "email");
        Directory.CreateDirectory(dir);
        var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var head = $"<!-- From: {WebUtility.HtmlEncode(email.FromAddress)}\n     To: {WebUtility.HtmlEncode(string.Join(", ", email.To))}" +
                   $"{(email.Cc.Count > 0 ? "\n     Cc: " + WebUtility.HtmlEncode(string.Join(", ", email.Cc)) : "")}\n     Subject: {WebUtility.HtmlEncode(email.Subject)} -->\n";
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".html"), head + email.HtmlBody, ct);
        log.LogInformation("Email {Subject} written to the pickup folder as {Id}", email.Subject, id);
        return new ProviderResult(true, "dev-" + id, null);
    }
}

/// <summary>Development SMS provider: appends each text to a log file instead of sending it.</summary>
public sealed class DevSmsProvider(IOptions<MessagingOptions> options, ILogger<DevSmsProvider> log) : ISmsProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public string Name => "dev";

    public async Task<ProviderResult> SendAsync(string fromNumber, string toNumber, string body, CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(options.Value.PickupDirectory);
        Directory.CreateDirectory(dir);
        var sid = "DEV" + Guid.NewGuid().ToString("N")[..24];
        await Gate.WaitAsync(ct);
        try { await File.AppendAllTextAsync(Path.Combine(dir, "sms.log"), $"{DateTime.UtcNow:u} {sid} {fromNumber} -> {toNumber}: {body.ReplaceLineEndings(" ")}\n", ct); }
        finally { Gate.Release(); }
        log.LogInformation("SMS {Sid} to {To} written to the pickup folder", sid, toNumber);
        return new ProviderResult(true, sid, null);
    }
}

/// <summary>Used when no provider is configured: every send fails with a clear message, nothing leaves the server.</summary>
public sealed class NotConfiguredProvider : IMailProvider, ISmsProvider
{
    public string Name => "none";
    private static readonly ProviderResult Fail = new(false, null, "Messaging isn't connected yet. An administrator needs to set it up.");
    public Task<ProviderResult> SendAsync(Guid userId, OutgoingEmail email, CancellationToken ct = default) => Task.FromResult(Fail);
    public Task<ProviderResult> SendAsync(string fromNumber, string toNumber, string body, CancellationToken ct = default) => Task.FromResult(Fail);
}
