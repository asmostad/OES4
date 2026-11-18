using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Messages;

/// <summary>Shared handlers for email, texts and consent, posted from record pages.</summary>
public class SendModel(MessagingService messaging, ConsentService consent) : CrmPageModel
{
    public IActionResult OnGet() => NotFound();

    private async Task<IActionResult> Run(string? returnUrl, Func<Task<string>> action)
    {
        try { Flash(await action()); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is CrmRuleException or RecordForbiddenException) { Flash(ex.Message, "danger"); }
        return BackTo(returnUrl, "/");
    }

    private string Outcome(SendResult r, string sent)
    {
        if (r.Ok) return sent;
        TempData["FlashType"] = "danger";
        return r.Error ?? "The message couldn't be sent.";
    }

    public Task<IActionResult> OnPostEmailAsync(EmailInput email, string? returnUrl) =>
        Run(returnUrl, async () => Outcome(await messaging.SendEmailAsync(email), "Email sent."));

    public Task<IActionResult> OnPostSmsAsync(SmsInput sms, string? returnUrl) =>
        Run(returnUrl, async () => Outcome(await messaging.SendSmsAsync(sms), "Text sent."));

    public Task<IActionResult> OnPostConsentAsync(Guid contactId, string channel, string purpose, string status, string source, string? evidence, string? returnUrl) =>
        Run(returnUrl, async () =>
        {
            await consent.RecordAsync(contactId, channel, purpose, status, source, evidence);
            return "Consent recorded.";
        });
}
