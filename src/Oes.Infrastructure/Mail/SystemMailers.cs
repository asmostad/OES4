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
        var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.html");
        var html = $"<!-- To: {System.Net.WebUtility.HtmlEncode(email.To)} -->\n<!-- Subject: {System.Net.WebUtility.HtmlEncode(email.Subject)} -->\n{email.HtmlBody}";
        await File.WriteAllTextAsync(path, html, ct);
        log.LogInformation("System email {Subject} written to {Path}", email.Subject, path);
    }
}
