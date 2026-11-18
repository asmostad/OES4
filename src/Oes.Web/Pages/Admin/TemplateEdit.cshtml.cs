using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.TemplatesManage)]
public class TemplateEditModel(TemplateService templates) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Channel { get; set; } = Channels.Email;
    [BindProperty] public TemplateInput Input { get; set; } = new();

    public bool IsSms => Input.Channel == Channels.Sms;

    public async Task<IActionResult> OnGetAsync() => await Guard(async () =>
    {
        if (Id is { } id)
        {
            var t = await templates.GetAsync(id);
            Input = new TemplateInput { Channel = t.Channel, Name = t.Name, Subject = t.Subject, Body = t.Body, Category = t.Category, IsActive = t.IsActive };
        }
        else
        {
            if (!Channels.All.Contains(Channel)) return NotFound();
            Input.Channel = Channel;
        }
        return Page();
    });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        if (Id is { } id) Input.Channel = (await templates.GetAsync(id)).Channel;
        await templates.SaveAsync(Id, Input);
        Flash("Template saved.");
        return RedirectToPage("/Admin/Templates", new { channel = Input.Channel });
    }, Page);

    public Task<IActionResult> OnPostDeleteAsync() => Guard(async () =>
    {
        var t = await templates.GetAsync(Id ?? Guid.Empty);
        await templates.DeleteAsync(t.Id);
        Flash("Template deleted.");
        return RedirectToPage("/Admin/Templates", new { channel = t.Channel });
    });
}
