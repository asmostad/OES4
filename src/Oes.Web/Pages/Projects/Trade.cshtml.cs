using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

public class TradeModel(ProjectService projects, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public TradeDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<Option> RemovalReasons { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(string? tab) => Guard(async () =>
    {
        Detail = await projects.GetTradeAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Trade, Id, tab, null);
        RemovalReasons = await lookups.ValuesAsync(Oes.Domain.Production.RemovalReasons.Type);
        return Page();
    });

    public Task<IActionResult> OnPostStageAsync(Guid stageId, string? note) => Guard(async () =>
    {
        var result = await projects.MoveTradeAsync(Id, stageId, note);
        Flash("Stage updated." + (result.Automations.Count > 0 ? " " + string.Join(" ", result.Automations) : ""));
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostScheduleAsync(string? description, DateOnly? materialDate, DateOnly? workDate, string? crewNote) => Guard(async () =>
    {
        await projects.UpdateTradeAsync(Id, description, materialDate, workDate, crewNote);
        Flash("Schedule saved.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostRemoveAsync(string reasonKey, string? note) => Guard(async () =>
    {
        await projects.RemoveFromBoardAsync(EntityTypes.Trade, Id, reasonKey, note);
        Flash("Removed from the board.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostRestoreAsync() => Guard(async () =>
    {
        await projects.RestoreToBoardAsync(EntityTypes.Trade, Id);
        Flash("Back on the board.");
        return RedirectToPage(new { id = Id });
    });
}
