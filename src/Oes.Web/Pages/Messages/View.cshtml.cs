using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Messages;

public class ViewModel(MessagingService messaging, Oes.Infrastructure.Work.TenantTime time) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Channel { get; set; } = ItemTypes.Email;
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public MessagingService.MessageView Message { get; private set; } = null!;
    public DateTime LocalAt { get; private set; }
    public string Back => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Channel is not (ItemTypes.Email or ItemTypes.Sms)) return NotFound();
        Message = await messaging.GetAsync(Channel, Id);
        LocalAt = await time.ToLocalAsync(Message.At);
        return Page();
    });
}
