using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Work;

/// <summary>Shared handlers for tasks and appointments posted from any page (record agenda, Tasks, Calendar).</summary>
public class ItemsModel(WorkService work, NotificationService notifications) : CrmPageModel
{
    public IActionResult OnGet() => NotFound();

    private async Task<IActionResult> Run(string? returnUrl, Func<Task> action, string? message)
    {
        try
        {
            await action();
            if (message is not null) Flash(message);
        }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is CrmRuleException or RecordForbiddenException) { Flash(ex.Message, "danger"); }
        return BackTo(returnUrl, "/Tasks");
    }

    public Task<IActionResult> OnPostTaskAsync(TaskInput task, string? returnUrl) => Run(returnUrl, () => work.CreateTaskAsync(task), "Task added.");

    public Task<IActionResult> OnPostTaskDoneAsync(Guid id, bool done, string? returnUrl) => Run(returnUrl, () => work.SetDoneAsync(id, done), null);

    public Task<IActionResult> OnPostAppointmentAsync(AppointmentInput appointment, string? returnUrl) =>
        Run(returnUrl, () => work.CreateAppointmentAsync(appointment), "Appointment scheduled.");

    public Task<IActionResult> OnPostUpdateTaskAsync(Guid id, TaskInput task, string? returnUrl) =>
        Run(returnUrl, () => work.UpdateTaskAsync(id, task), "Task saved.");

    public Task<IActionResult> OnPostUpdateAppointmentAsync(Guid id, AppointmentInput appointment, string? returnUrl) =>
        Run(returnUrl, () => work.UpdateAppointmentAsync(id, appointment), "Appointment saved.");

    public Task<IActionResult> OnPostAppointmentStatusAsync(Guid id, string status, string? returnUrl) =>
        Run(returnUrl, () => work.SetAppointmentStatusAsync(id, status), "Appointment updated.");

    /// <summary>Opens a notification: marks it read and goes to its (always app-relative) link.</summary>
    public async Task<IActionResult> OnGetNotificationAsync(Guid id)
    {
        var url = await notifications.OpenAsync(id);
        return LocalRedirect(url is not null && Url.IsLocalUrl(url) ? url : "/");
    }

    public async Task<IActionResult> OnPostReadAllAsync(string? returnUrl)
    {
        await notifications.MarkAllReadAsync();
        return BackTo(returnUrl, "/");
    }
}
