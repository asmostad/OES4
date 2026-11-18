using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Nurture;

/// <summary>Enroll / stop handlers posted from contact and opportunity pages.</summary>
public class ItemsModel(NurtureService nurture) : CrmPageModel
{
    public IActionResult OnGet() => NotFound();

    private async Task<IActionResult> Run(string? returnUrl, Func<Task> action, string message)
    {
        try { await action(); Flash(message); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is CrmRuleException or RecordForbiddenException) { Flash(ex.Message, "danger"); }
        return BackTo(returnUrl, "/");
    }

    public Task<IActionResult> OnPostEnrollAsync(Guid sequenceId, Guid contactId, Guid? opportunityId, string? returnUrl) =>
        Run(returnUrl, () => nurture.EnrollAsync(sequenceId, contactId, opportunityId), "Enrolled.");

    public Task<IActionResult> OnPostStopAsync(Guid id, string? returnUrl) =>
        Run(returnUrl, () => nurture.StopAsync(id), "Stopped.");
}
