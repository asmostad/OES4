using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Blasts;

/// <summary>Write a draft, see who it reaches, send yourself a test, then schedule it.</summary>
[HasPermission(Permissions.BlastsManage)]
public class EditModel(BlastService blasts, TemplateService templates, CrmLookups lookups, StormService storms, TenantTime time) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public BlastInput Input { get; set; } = new();

    public AudiencePreview? Preview { get; private set; }
    public List<MessageTemplate> Templates { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<StormRow> Storms { get; private set; } = [];
    public DateOnly Today { get; private set; }

    private async Task LoadAsync()
    {
        Templates = await templates.ListAsync(Channels.Email, activeOnly: true);
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Storms = await storms.ListAsync();
        Today = await time.TodayAsync();
        if (Id is not null) Preview = await blasts.PreviewAsync(Input.Audience);
    }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var b = await blasts.GetAsync(id);
            if (b.Status != BlastStatuses.Draft) return RedirectToPage("/Blasts/View", new { id });
            Input = new BlastInput { Name = b.Name, TemplateId = b.TemplateId, Subject = b.Subject, Body = b.Body, Audience = BlastService.Audience(b), ThrottlePerMinute = b.ThrottlePerMinute };
        }
        else
        {
            Input.Audience.Statuses = [ContactStatuses.Customer, ContactStatuses.PastCustomer];
        }
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync() => await LoadThen(async () =>
    {
        Id = await blasts.SaveDraftAsync(Id, Input);
        Flash("Draft saved. Check who it reaches, then send a test.");
        return RedirectToPage(new { id = Id });
    });

    public async Task<IActionResult> OnPostTestAsync() => await LoadThen(async () =>
    {
        Id = await blasts.SaveDraftAsync(Id, Input);
        var to = await blasts.SendTestAsync(Id.Value);
        Flash($"Test sent to {to}.");
        return RedirectToPage(new { id = Id });
    });

    public async Task<IActionResult> OnPostScheduleAsync(string when, DateOnly? date, TimeOnly? at) => await LoadThen(async () =>
    {
        Id = await blasts.SaveDraftAsync(Id, Input);
        DateTime? sendAt = null;
        if (when == "later")
        {
            if (date is null || at is null) throw new CrmRuleException("Pick a date and time.");
            sendAt = await time.ToUtcAsync(date.Value, at.Value);
        }
        var n = await blasts.ScheduleAsync(Id.Value, sendAt);
        Flash(sendAt is null ? $"Sending to {n} people now." : $"Scheduled for {n} people.");
        return RedirectToPage("/Blasts/View", new { id = Id });
    });

    /// <summary>Loads the page's lists first, so a rule error can redisplay the form with the message.</summary>
    private async Task<IActionResult> LoadThen(Func<Task<IActionResult>> action)
    {
        await LoadAsync();
        return await Guard(action, Page);
    }

    public Task<IActionResult> OnPostDeleteAsync() => Guard(async () =>
    {
        await blasts.DeleteDraftAsync(Id ?? Guid.Empty);
        Flash("Draft deleted.");
        return RedirectToPage("/Blasts/Index");
    });
}
