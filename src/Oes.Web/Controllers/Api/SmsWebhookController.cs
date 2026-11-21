using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Oes.Infrastructure.Communication;

namespace Oes.Web.Controllers.Api;

/// <summary>
/// Inbound texts. The development provider posts From/To/Body/MessageSid with a shared secret header.
/// The Twilio endpoint (with request-signature validation) is added with the Twilio provider.
/// </summary>
[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("portal")]
[Route("api/webhooks/sms")]
public sealed class SmsWebhookController(MessagingService messaging, IOptions<MessagingOptions> options) : ControllerBase
{
    public const string SecretHeader = "X-Oes-Webhook-Secret";

    [HttpPost("dev")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Dev([FromForm(Name = "From")] string? from, [FromForm(Name = "To")] string? to,
        [FromForm(Name = "Body")] string? body, [FromForm(Name = "MessageSid")] string? sid, CancellationToken ct)
    {
        var o = options.Value;
        if (!string.Equals(o.SmsProvider, "Dev", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(o.DevWebhookSecret)) return NotFound();
        var given = Request.Headers[SecretHeader].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(o.DevWebhookSecret))) return Unauthorized();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return BadRequest();
        if (sid is { Length: > 100 }) return BadRequest();

        // Unknown numbers get the same answer, so the endpoint can't be used to discover customers' numbers.
        await messaging.ReceiveSmsAsync(from, to, body, sid, ct);
        return Ok();
    }
}
