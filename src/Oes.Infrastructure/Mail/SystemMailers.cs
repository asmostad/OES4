using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Mail;

public sealed class SystemMailOptions
{
    public const string Section = "SystemMail";

    /// <summary>"Ses" in production, "Pickup" in development (writes .html files to a folder).</summary>
    public string Provider { get; set; } = "Pickup";

    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "Pich";

    /// <summary>AWS region for SES, e.g. us-east-2. Credentials come from the standard AWS chain, never config files.</summary>
    public string? SesRegion { get; set; }

    /// <summary>SES configuration set for bounce/complaint events.</summary>
    public string? SesConfigurationSet { get; set; }

    public string PickupDirectory { get; set; } = "mail-pickup";
}

/// <summary>Sends platform email through Amazon SES (v2 API).</summary>
public sealed class SesSystemMailer(IAmazonSimpleEmailServiceV2 ses, IOptions<SystemMailOptions> options, ILogger<SesSystemMailer> log)
    : ISystemMailer
{
    public async Task SendAsync(SystemEmail email, CancellationToken ct = default)
    {
        var o = options.Value;
        var request = new SendEmailRequest
        {
            FromEmailAddress = $"{o.FromName} <{o.FromAddress}>",
            Destination = new Destination { ToAddresses = [email.To] },
            ConfigurationSetName = o.SesConfigurationSet,
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = email.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Html = new Content { Data = email.HtmlBody, Charset = "UTF-8" },
                        Text = email.TextBody is null ? null : new Content { Data = email.TextBody, Charset = "UTF-8" },
                    },
                    Attachments = email.Attachments is { Count: > 0 } files
                        ? files.Select(f => new Amazon.SimpleEmailV2.Model.Attachment
                        {
                            FileName = f.FileName, ContentType = f.ContentType, RawContent = new MemoryStream(f.Content),
                            ContentDisposition = AttachmentContentDisposition.ATTACHMENT, ContentTransferEncoding = AttachmentContentTransferEncoding.BASE64,
                        }).ToList()
                        : null,
                },
            },
        };
        var response = await ses.SendEmailAsync(request, ct);
        log.LogInformation("System email {Subject} sent via SES, message {MessageId}", email.Subject, response.MessageId);
    }
}

/// <summary>Development mailer: writes each message to an .html file instead of sending it.</summary>
public sealed class PickupSystemMailer(IOptions<SystemMailOptions> options, ILogger<PickupSystemMailer> log) : ISystemMailer
{
    public async Task SendAsync(SystemEmail email, CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(options.Value.PickupDirectory);
        Directory.CreateDirectory(dir);
        var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var path = Path.Combine(dir, id + ".html");
        var files = email.Attachments ?? [];
        var html = $"<!-- To: {System.Net.WebUtility.HtmlEncode(email.To)} -->\n<!-- Subject: {System.Net.WebUtility.HtmlEncode(email.Subject)} -->\n"
                   + string.Concat(files.Select((f, i) => $"<!-- Attachment: {System.Net.WebUtility.HtmlEncode(f.FileName)} -> {id}-{i + 1}-{Communication.DevMailProvider.SafeName(f.FileName)} -->\n"))
                   + email.HtmlBody;
        await File.WriteAllTextAsync(path, html, ct);
        var n = 0;
        foreach (var f in files) await File.WriteAllBytesAsync(Path.Combine(dir, $"{id}-{++n}-{Communication.DevMailProvider.SafeName(f.FileName)}"), f.Content, ct);
        log.LogInformation("System email {Subject} written to {Path}", email.Subject, path);
    }
}
