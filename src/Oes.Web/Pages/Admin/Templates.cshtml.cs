using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.TemplatesManage)]
public class TemplatesModel(TemplateService templates, MessagingService messaging, ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Channel { get; set; } = Channels.Email;

    public List<MessageTemplate> Templates { get; private set; } = [];
    public string? PhoneNumber { get; private set; }
    public bool CanSetNumber { get; private set; }
    public bool IsSms => Channel == Channels.Sms;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Channels.All.Contains(Channel)) return NotFound();
        Templates = await templates.ListAsync(Channel);
        CanSetNumber = await user.HasPermissionAsync(Permissions.SettingsManage);
        PhoneNumber = await messaging.PhoneNumberAsync();
        return Page();
    }

    public Task<IActionResult> OnPostNumberAsync(string? number) => Guard(async () =>
    {
        await messaging.SetPhoneNumberAsync(number);
        Flash(string.IsNullOrWhiteSpace(number) ? "Texting turned off." : "Texting number saved.");
        return RedirectToPage(new { channel = Channels.Sms });
    });
}
