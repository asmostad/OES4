using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.NurtureManage)]
public class NurtureEditModel(NurtureService nurture, TenantTime time) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public SequenceInput Input { get; set; } = new();

    public Sequence Sequence { get; private set; } = null!;
    public List<EnrollmentRow> Enrollments { get; private set; } = [];
    public Dictionary<Guid, DateTime> NextLocal { get; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Sequence = await nurture.GetAsync(Id);
        Input = new SequenceInput
        {
            Name = Sequence.Name, Description = Sequence.Description, Category = Sequence.Category, IsActive = Sequence.IsActive,
            StopOnReply = Sequence.StopOnReply, StopWhenClosed = Sequence.StopWhenClosed,
        };
        Enrollments = await nurture.EnrollmentsAsync(null, null, Id, 50);
        foreach (var e in Enrollments.Where(e => e.NextRunAt is not null)) NextLocal[e.Id] = await time.ToLocalAsync(e.NextRunAt!.Value);
        return Page();
    });


    /// <summary>Runs a change and comes back to this workflow, flashing either the result or the rule that stopped it.</summary>
    private async Task<IActionResult> Run(Func<Task> action, string? message)
    {
        try { await action(); if (message is not null) Flash(message); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is CrmRuleException or RecordForbiddenException) { Flash(ex.Message, "danger"); }
        return RedirectToPage(new { id = Id });
    }

    public Task<IActionResult> OnPostAsync() => Run(() => nurture.SaveAsync(Id, Input), "Saved.");

    public Task<IActionResult> OnPostAddStepAsync(StepInput step) => Run(() => nurture.AddStepAsync(Id, step), "Step added.");

    public Task<IActionResult> OnPostUpdateStepAsync(Guid stepId, StepInput step) => Run(() => nurture.UpdateStepAsync(stepId, step), "Step saved.");

    public Task<IActionResult> OnPostDeleteStepAsync(Guid stepId) => Run(() => nurture.DeleteStepAsync(stepId), "Step removed.");

    public Task<IActionResult> OnPostMoveStepAsync(Guid stepId, int direction) => Run(() => nurture.MoveStepAsync(stepId, direction), null);

    public Task<IActionResult> OnPostStopAsync(Guid enrollmentId) => Run(() => nurture.StopAsync(enrollmentId), "Stopped.");
}
