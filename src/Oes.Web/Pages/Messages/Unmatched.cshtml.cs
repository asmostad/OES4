using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Messages;

[HasPermission(Permissions.SmsViewAll)]
public class UnmatchedModel(UnmatchedTextService texts, MessagingService messaging) : CrmPageModel
{
    public List<UnmatchedThread> Threads { get; private set; } = [];

    public async Task OnGetAsync() => Threads = await texts.ListAsync();

    public Task<IActionResult> OnPostLinkAsync(string number, Guid contactId) => Guard(async () =>
    {
        await texts.LinkAsync(number, contactId);
        Flash("Texts filed on the contact, and the number added to them.");
        return RedirectToPage("/Contacts/View", new { id = contactId });
    });

    public Task<IActionResult> OnPostNewLeadAsync(string number, string? firstName, string? lastName, bool createOpportunity) => Guard(async () =>
    {
        var (contactId, oppId) = await texts.NewLeadAsync(number, firstName, lastName, createOpportunity);
        Flash(oppId is null ? "Contact created." : "Contact and lead created.");
        return oppId is { } o ? RedirectToPage("/Opportunities/View", new { id = o }) : RedirectToPage("/Contacts/View", new { id = contactId });
    });

    public Task<IActionResult> OnPostReplyAsync(string number, string body) => Guard(async () =>
    {
        var sent = await messaging.ReplyToUnknownAsync(number, body);
        Flash(sent.Ok ? "Reply sent." : $"The text didn't go through: {sent.Error}", sent.Ok ? "success" : "danger");
        return RedirectToPage();
    });

    public Task<IActionResult> OnPostDismissAsync(string number) => Guard(async () =>
    {
        await texts.DismissAsync(number);
        Flash("Dismissed. A new text from that number will show up again.");
        return RedirectToPage();
    });
}
