using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Production;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record ChangeOrderItem(Guid Id, int Sequence, string Description, decimal Amount, string Status, bool IsItemized, bool IsInsuranceSupplement,
    Guid? TradeId, string? ApprovedByName, DateTime? DecidedAt);
public sealed record ChangeOrderList(bool CanAdd, decimal ContractValue, List<ChangeOrderItem> Items);
public sealed record ChangeOrderLineDto(Guid Id, string? Code, string Description, decimal Qty, string Unit, decimal UnitPrice, decimal Amount);
public sealed record ChangeOrderMobileDto(Guid Id, int Sequence, Guid ProjectId, string ProjectNumber, string CustomerName, string? Address, string? TradeName,
    string Description, string? Reason, decimal Amount, string Status, bool IsItemized, bool IsInsuranceSupplement, string? ApprovedByName,
    DateTime? DecidedAt, List<ChangeOrderLineDto> Lines, bool CanDecide);
public sealed record NewChangeOrderInput(Guid? TradeId, string? Description, decimal Amount, string? Reason, bool IsInsuranceSupplement);
public sealed record DecideChangeOrderInput(bool Approve, string? ApprovedByName);

/// <summary>
/// Change orders from the field app: what's on a job, a new one for extra work agreed on site (one amount; itemizing
/// from the price list stays on the web), and the customer's yes or no. The PDF comes from the change order page's
/// API route (handler=Pdf).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ChangeOrdersApiController(ProjectService projects, ICurrentUser user) : ControllerBase
{
    [HttpGet("projects/{id:guid}/change-orders")]
    public async Task<ChangeOrderList> List(Guid id, CancellationToken ct)
    {
        var d = await projects.GetAsync(id, ct);
        return new ChangeOrderList(d.CanEdit, d.Project.ContractValue, d.ChangeOrders.Select(c => new ChangeOrderItem(c.Id, c.Sequence, c.Description, c.Amount, c.Status,
            c.IsItemized, c.IsInsuranceSupplement, c.ProjectTradeId, c.ApprovedByName, c.DecidedAt)).ToList());
    }

    [HttpGet("change-orders/{id:guid}")]
    public async Task<ChangeOrderMobileDto> Get(Guid id, CancellationToken ct)
    {
        var d = await projects.GetChangeOrderAsync(id, ct);
        var c = d.ChangeOrder;
        var canDecide = c.Status == ChangeOrderStatuses.Draft && d.Project.ArchivedAt is null
            && await user.HasPermissionAsync(Permissions.FinancialsEdit, ct) && (await projects.GetAsync(d.Project.Id, ct)).CanEdit;
        return new ChangeOrderMobileDto(c.Id, c.Sequence, d.Project.Id, d.Project.Number, d.CustomerName, d.Address, d.TradeName, c.Description, c.Reason,
            c.Amount, c.Status, c.IsItemized, c.IsInsuranceSupplement, c.ApprovedByName, c.DecidedAt,
            d.Lines.Select(l => new ChangeOrderLineDto(l.Id, l.Code, l.Description, l.Qty, l.Unit, l.UnitPrice, l.Amount)).ToList(), canDecide);
    }

    /// <summary>A change order for one amount (negative for a credit), as a draft until the customer decides.</summary>
    [HttpPost("projects/{id:guid}/change-orders")]
    [Idempotent]
    public async Task<StartedDto> Add(Guid id, NewChangeOrderInput input, CancellationToken ct) =>
        new(await projects.AddChangeOrderAsync(id, input.TradeId, input.Description ?? "", input.Amount, input.Reason, input.IsInsuranceSupplement, ct));

    /// <summary>The customer's decision; approving needs who approved it and adds to the contract.</summary>
    [HttpPost("change-orders/{id:guid}/decide")]
    [Idempotent]
    public async Task<IActionResult> Decide(Guid id, DecideChangeOrderInput input, CancellationToken ct)
    {
        await projects.DecideChangeOrderAsync(id, input.Approve, input.ApprovedByName, ct);
        return NoContent();
    }
}
