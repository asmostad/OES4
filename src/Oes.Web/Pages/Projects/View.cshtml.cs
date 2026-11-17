using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

public class ViewModel(ProjectService projects, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public ProjectDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<Option> RemovalReasons { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(string? tab, string? range) => Guard(async () =>
    {
        Detail = await projects.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Project, Id, tab, range);
        RemovalReasons = await lookups.ValuesAsync(Oes.Domain.Production.RemovalReasons.Type);
        return Page();
    });

    private Task<IActionResult> Do(Func<Task> action, string? message) => Guard(async () =>
    {
        await action();
        if (message is not null) Flash(message);
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostStageAsync(Guid stageId, string? note) => Do(() => projects.MoveProjectAsync(Id, stageId, note), "Stage updated.");

    public Task<IActionResult> OnPostTradeStageAsync(Guid tradeId, Guid stageId) => Guard(async () =>
    {
        var result = await projects.MoveTradeAsync(tradeId, stageId, null);
        Flash("Trade moved." + (result.Automations.Count > 0 ? " " + string.Join(" ", result.Automations) : ""));
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostRemoveAsync(string reasonKey, string? note) =>
        Do(() => projects.RemoveFromBoardAsync(EntityTypes.Project, Id, reasonKey, note), "Removed from the board. It stays in the list and can be restored.");

    public Task<IActionResult> OnPostRestoreAsync() => Do(() => projects.RestoreToBoardAsync(EntityTypes.Project, Id), "Back on the board.");

    public Task<IActionResult> OnPostChangeOrderAsync(Guid? tradeId, string description, decimal amount, string? reason, bool isSupplement) =>
        Do(() => projects.AddChangeOrderAsync(Id, tradeId, description, amount, reason, isSupplement), "Change order added. Record the customer's approval to add it to the contract.");

    public Task<IActionResult> OnPostDecideChangeOrderAsync(Guid changeOrderId, bool approve, string? approvedByName) =>
        Do(() => projects.DecideChangeOrderAsync(changeOrderId, approve, approvedByName), approve ? "Change order approved." : "Change order rejected.");
}
