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
    public List<Disposition> Dispositions { get; private set; } = [];

    public List<Oes.Domain.Production.WorkflowStage> WorkflowStages { get; private set; } = [];
    public string? WorkflowLevel => List switch { "project_stages" => Oes.Domain.Production.WorkflowLevels.Project, "trade_stages" => Oes.Domain.Production.WorkflowLevels.Trade, _ => null };

    public Task<IActionResult> OnPostWorkflowStageAsync(Guid? id, string name, string? role, string color, int? staleAfterDays, bool isActive) =>
        Run(() => settings.SaveWorkflowStageAsync(WorkflowLevel!, id, name, role ?? "", color, staleAfterDays, isActive), "Stage saved.");

    public Task<IActionResult> OnPostMoveWorkflowStageAsync(Guid id, int direction) =>
        Run(() => settings.MoveWorkflowStageAsync(WorkflowLevel!, id, direction), null);

    public async Task OnGetAsync()
    {
        if (WorkflowLevel is { } level) { WorkflowStages = await settings.WorkflowStagesAsync(level); return; }
        if (List == "dispositions")
        {
            Dispositions = await settings.DispositionsAsync();
            Stages = await settings.StagesAsync();
            Values = await settings.ValuesAsync(LookupTypes.LostReason);
            return;
        }
        if (List == "stages") Stages = await settings.StagesAsync();
        else if (ListSettingsService.EditableLists.ContainsKey(List)) Values = await settings.ValuesAsync(List);
        else List = "stages";
        if (List == "stages" && Stages.Count == 0) Stages = await settings.StagesAsync();
    }

    public Task<IActionResult> OnPostStageAsync(StageInput stage) => Run(() => settings.SaveStageAsync(stage), "Stage saved.");

    public Task<IActionResult> OnPostMoveAsync(Guid id, int direction) => Run(() => settings.MoveStageAsync(id, direction), null);

    public Task<IActionResult> OnPostDefaultAsync(Guid id) => Run(() => settings.SetDefaultStageAsync(id), "Default stage changed.");

    public Task<IActionResult> OnPostDispositionAsync(DispositionInput disposition) =>
        Run(() => settings.SaveDispositionAsync(disposition), "Call outcome saved.");

    public Task<IActionResult> OnPostMoveDispositionAsync(Guid id, int direction) => Run(() => settings.MoveDispositionAsync(id, direction), null);

    public Task<IActionResult> OnPostValueAsync(Guid? id, string label, bool isActive) =>
        Run(() => settings.SaveValueAsync(List, id, label, isActive), "Saved.");

    private Task<IActionResult> Run(Func<Task> action, string? success) => Guard(async () =>
    {
        await action();
        if (success is not null) Flash(success);
        return RedirectToPage(new { list = List });
    });
}
