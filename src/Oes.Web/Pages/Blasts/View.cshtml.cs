using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Blasts;

[HasPermission(Permissions.BlastsManage)]
public class ViewModel(BlastService blasts, TenantTime time) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public Blast Blast { get; private set; } = null!;
    public List<BlastRecipientRow> Recipients { get; private set; } = [];
    public DateTime? ScheduledLocal { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Blast = await blasts.GetAsync(Id);
        if (Blast.Status == BlastStatuses.Draft) return RedirectToPage("/Blasts/Edit", new { id = Id });
        Recipients = await blasts.RecipientsAsync(Id, Status);
        ScheduledLocal = Blast.ScheduledAt is { } s ? await time.ToLocalAsync(s) : null;
        return Page();
    });

    public Task<IActionResult> OnPostCancelAsync() => Guard(async () =>
    {
        await blasts.CancelAsync(Id);
        Flash("Blast cancelled. Emails already sent can't be recalled.");
        return RedirectToPage(new { id = Id });
    });
}
