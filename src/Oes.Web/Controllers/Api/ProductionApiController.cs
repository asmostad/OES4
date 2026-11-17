using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Production;

namespace Oes.Web.Controllers.Api;

public sealed record MoveRequest(Guid StageId, string? Note);

/// <summary>Board drag-and-drop for projects and trades. Same gates and automation as the record pages.</summary>
[ApiController]
[Route("api/v1")]
public sealed class ProductionApiController(ProjectService projects) : ControllerBase
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

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (RecordForbiddenException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (CrmRuleException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest); }
    }
}
