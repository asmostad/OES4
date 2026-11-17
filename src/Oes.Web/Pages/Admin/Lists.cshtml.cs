using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.SettingsManage)]
public class ListsModel(ListSettingsService settings) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string List { get; set; } = "stages";

    public List<PipelineStage> Stages { get; private set; } = [];
    public List<LookupValue> Values { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (List == "stages") Stages = await settings.StagesAsync();
        else if (ListSettingsService.EditableLists.ContainsKey(List)) Values = await settings.ValuesAsync(List);
        else List = "stages";
        if (List == "stages" && Stages.Count == 0) Stages = await settings.StagesAsync();
    }

    public Task<IActionResult> OnPostStageAsync(StageInput stage) => Run(() => settings.SaveStageAsync(stage), "Stage saved.");

    public Task<IActionResult> OnPostMoveAsync(Guid id, int direction) => Run(() => settings.MoveStageAsync(id, direction), null);

    public Task<IActionResult> OnPostDefaultAsync(Guid id) => Run(() => settings.SetDefaultStageAsync(id), "Default stage changed.");

    public Task<IActionResult> OnPostValueAsync(Guid? id, string label, bool isActive) =>
        Run(() => settings.SaveValueAsync(List, id, label, isActive), "Saved.");

    private Task<IActionResult> Run(Func<Task> action, string? success) => Guard(async () =>
    {
        await action();
        if (success is not null) Flash(success);
        return RedirectToPage(new { list = List });
    });
}
