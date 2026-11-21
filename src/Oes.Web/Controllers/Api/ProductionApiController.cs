using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;

namespace Oes.Web.Controllers.Api;

public sealed record MoveRequest(Guid StageId, string? Note);
public sealed record StageDto(Guid Id, string Name, string Color, string Role);
public sealed record HistoryDto(DateTime At, string? From, string To, string? By, bool IsAutomatic, string? Note);
public sealed record ProjectListItem(Guid Id, string Number, string Name, string StageName, string StageColor, string ContactName, string? Address,
    int Trades, int TradesComplete, bool IsStale);
public sealed record TradeSummaryDto(Guid Id, string Name, string Color, Guid StageId, string StageName, string StageColor, DateOnly? MaterialDate,
    DateOnly? WorkDate, string? CrewNote, bool IsStale, bool IsComplete);
public sealed record ProjectMobileDto(Guid Id, string Number, string Name, StageDto Stage, Guid OpportunityId, Guid ContactId, string ContactName,
    List<ContactMethodDto> ContactMethods, Guid? PropertyId, string? Address, string? OwnerName, string? Notes, List<TradeSummaryDto> Trades,
    List<StageDto> Stages, List<HistoryDto> History, bool CanAdvance, bool IsRemoved);
public sealed record TradeMobileDto(Guid Id, string Name, string Color, Guid ProjectId, string ProjectNumber, string ProjectName, StageDto Stage,
    string? Description, DateOnly? MaterialDate, DateOnly? WorkDate, string? CrewNote, Guid ContactId, string ContactName,
    List<ContactMethodDto> ContactMethods, Guid? PropertyId, string? Address, List<StageDto> Stages, List<HistoryDto> History, bool CanAdvance, bool IsRemoved);
public sealed record ScheduledTrade(Guid Id, Guid ProjectId, string ProjectNumber, string ProjectName, string Name, string Color, string StageName,
    string StageColor, string? Address, DateOnly? MaterialDate, DateOnly? WorkDate, bool IsStale);

/// <summary>
/// Projects and trades: board drag-and-drop on the web, and the field app's job screens.
/// Same gates, automation and record scope as the record pages.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ProductionApiController(ProjectService projects, OesDbContext db) : ControllerBase
{
    [HttpGet("trades/{id:guid}/stage-preview")]
    public Task<IActionResult> Preview(Guid id, Guid stageId, CancellationToken ct) =>
        Run(async () => Ok(await projects.PreviewTradeMoveAsync(id, stageId, ct)));

    [HttpPut("trades/{id:guid}/stage")]
    public Task<IActionResult> MoveTrade(Guid id, MoveRequest body, CancellationToken ct) =>
        Run(async () => Ok(await projects.MoveTradeAsync(id, body.StageId, body.Note, ct)));

    [HttpPut("projects/{id:guid}/stage")]
    public Task<IActionResult> MoveProject(Guid id, MoveRequest body, CancellationToken ct) =>
        Run(async () => { await projects.MoveProjectAsync(id, body.StageId, body.Note, ct); return NoContent(); });

    /// <summary>Projects the user can see on the board, newest first.</summary>
    [HttpGet("projects")]
    public async Task<List<ProjectListItem>> Projects(string? q, bool mine = false, CancellationToken ct = default) =>
        (await projects.ListAsync(q, null, false, mine, null, ct)).Take(100)
            .Select(p => new ProjectListItem(p.Id, p.Number, p.Name, p.StageName, p.StageColor, p.ContactName, p.Address, p.Trades, p.TradesComplete, p.IsStale))
            .ToList();

    /// <summary>The crew's schedule: open trades on the board, soonest work date first (undated last).</summary>
    [HttpGet("trades")]
    public async Task<List<ScheduledTrade>> Schedule(bool mine = false, bool includeComplete = false, CancellationToken ct = default)
    {
        var board = await projects.TradeBoardAsync(null, mine, ct);
        return board
            .Where(c => includeComplete || c.Stage.Role != StageRoles.Complete)
            .SelectMany(c => c.Cards.Select(t => new ScheduledTrade(t.Id, t.ProjectId, t.ProjectNumber, t.ProjectName, t.TradeName, t.TradeColor,
                c.Stage.Name, c.Stage.Color, t.Address, t.MaterialDate, t.WorkDate, t.IsStale)))
            .OrderBy(t => t.WorkDate ?? DateOnly.MaxValue).ThenBy(t => t.ProjectNumber).Take(300).ToList();
    }

    [HttpGet("projects/{id:guid}")]
    public Task<IActionResult> Project(Guid id, CancellationToken ct) => Run(async () =>
    {
        var d = await projects.GetAsync(id, ct);
        var p = d.Project;
        return Ok(new ProjectMobileDto(p.Id, p.Number, p.Name, Stage(d.Stage), d.Opportunity.Id, d.Contact.Id, d.Contact.DisplayName,
            await MethodsAsync(d.Contact.Id, ct), d.Property?.Id, d.Property?.SingleLine, d.OwnerName, p.Notes,
            d.Trades.Select(t => new TradeSummaryDto(t.Trade.Id, t.Type.Name, t.Type.Color, t.Stage.Id, t.Stage.Name, t.Stage.Color,
                t.Trade.MaterialDate, t.Trade.WorkDate, t.Trade.CrewNote, t.IsStale, t.Trade.CompletedAt != null)).ToList(),
            d.ProjectStages.Select(Stage).ToList(), History(d.History), d.CanAdvance, p.RemovedFromBoardAt != null));
    });

    [HttpGet("trades/{id:guid}")]
    public Task<IActionResult> Trade(Guid id, CancellationToken ct) => Run(async () =>
    {
        var d = await projects.GetTradeAsync(id, ct);
        var t = d.Trade;
        return Ok(new TradeMobileDto(t.Id, d.Type.Name, d.Type.Color, d.Project.Id, d.Project.Number, d.Project.Name, Stage(d.Stage),
            t.Description, t.MaterialDate, t.WorkDate, t.CrewNote, d.Contact.Id, d.Contact.DisplayName, await MethodsAsync(d.Contact.Id, ct),
            d.Property?.Id, d.Property?.SingleLine, d.Stages.Select(Stage).ToList(), History(d.History), d.CanAdvance, t.RemovedFromBoardAt != null));
    });

    private static StageDto Stage(WorkflowStage s) => new(s.Id, s.Name, s.Color, s.Role);

    private static List<HistoryDto> History(List<HistoryRow> rows) =>
        rows.Take(20).Select(h => new HistoryDto(h.At, h.From, h.To, h.By, h.IsAutomatic, h.Note)).ToList();

    private Task<List<ContactMethodDto>> MethodsAsync(Guid contactId, CancellationToken ct) =>
        db.ContactMethods.Where(m => m.ContactId == contactId).OrderByDescending(m => m.IsPrimary)
            .Select(m => new ContactMethodDto(m.Kind, m.Value, m.IsPrimary)).ToListAsync(ct);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (RecordForbiddenException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (CrmRuleException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest); }
    }
}
