using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Production;

public sealed record OrderSheet(TradeDetail Trade, List<OrderSheetLine> Lines, TakeoffRun Takeoff, int EstimateLines, string? OrderedBy);

/// <summary>
/// A trade's materials order. What was sold sets the price; this is what production buys. It can be
/// filled from the takeoff rules (the property's current measurements for the trade, all buildings),
/// copied from the sold estimate's product lines, or typed in, then printed for the supplier and
/// marked ordered. Refilling replaces only the lines that source made; hand-added lines stay.
/// </summary>
public sealed class OrderSheetService(OesDbContext db, ProjectService projects, TakeoffService takeoff, RecordItems items, IClock clock, RecordScope scope)
{
    public async Task<OrderSheet> GetAsync(Guid tradeId, TakeoffChoicesInput? choices = null, CancellationToken ct = default)
    {
        var detail = await projects.GetTradeAsync(tradeId, ct);
        var lines = await db.OrderSheetLines.Where(l => l.ProjectTradeId == tradeId).OrderBy(l => l.Sort).ToListAsync(ct);
        var run = await TakeoffAsync(detail.Trade, detail.Project, choices, ct);
        var estimateLines = await EstimateLinesFor(detail.Project, detail.Trade).CountAsync(ct);
        var orderedBy = detail.Trade.OrderedByUserId is { } u
            ? await db.Users.Where(x => x.Id == u).Select(x => x.FirstName + " " + x.LastName).FirstOrDefaultAsync(ct) : null;
        return new OrderSheet(detail, lines, run, estimateLines, orderedBy);
    }

    private async Task<TakeoffRun> TakeoffAsync(ProjectTrade trade, Project project, TakeoffChoicesInput? choices, CancellationToken ct)
    {
        // Every building's current measurements for this trade, added together.
        var measured = project.PropertyId is not { } pid ? new Dictionary<string, decimal>()
            : await (from v in db.MeasurementValues
                     join s in db.MeasurementSets on v.SetId equals s.Id
                     where s.PropertyId == pid && s.TradeTypeId == trade.TradeTypeId && s.IsCurrent
                     group v by v.FieldKey into g
                     select new { g.Key, Sum = g.Sum(x => x.Value) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        return await takeoff.BuildAsync(trade.TradeTypeId, measured, trade.OrderTakeoffJson, choices, ct);
    }

    private IQueryable<EstimateLine> EstimateLinesFor(Project project, ProjectTrade trade)
    {
        var estimateId = project.EstimateId ?? Guid.Empty;
        var tradeTypeId = trade.TradeTypeId;
        return from l in db.EstimateLines
               join s in db.EstimateSections on l.SectionId equals s.Id
               where l.EstimateId == estimateId && s.TradeTypeId == tradeTypeId && l.ProductId != null && l.IsMaterial && l.Qty > 0
               orderby s.Sort, l.Sort
               select l;
    }

    /// <summary>Replaces the takeoff-made lines with a new run. Returns how many lines were added.</summary>
    public async Task<int> FillFromTakeoffAsync(Guid tradeId, TakeoffChoicesInput choices, CancellationToken ct = default)
    {
        var (trade, project) = await projects.TradeForAsync(tradeId, advance: false, ct);
        var run = await TakeoffAsync(trade, project, choices, ct);
        if (!run.HasMeasurements) throw new CrmRuleException("The property has no measurements for this trade yet.");
        var lines = run.Lines.Where(l => l.Problem is null && l.Product is not null)
            .Select(l => new OrderSheetLine
            {
                Source = OrderLineSources.Takeoff, ProductId = l.Product!.Id, TakeoffRuleId = l.Rule.Id, Sku = l.Product.Sku,
                Description = l.Description, Qty = l.Qty, Unit = l.Product.Unit, UnitCost = l.Product.DefaultCost,
            }).ToList();
        trade.OrderTakeoffJson = TakeoffService.Write(choices);
        await ReplaceAsync(trade, OrderLineSources.Takeoff, lines, ct);
        await items.LogAsync(EntityTypes.Trade, tradeId, "order_sheet.takeoff", $"Order sheet filled from the takeoff: {lines.Count} line{(lines.Count == 1 ? "" : "s")}", ct);
        await db.SaveChangesAsync(ct);
        return lines.Count;
    }

    /// <summary>Replaces the estimate-copied lines with the sold estimate's material product lines for this trade.</summary>
    public async Task<int> CopyFromEstimateAsync(Guid tradeId, CancellationToken ct = default)
    {
        var (trade, project) = await projects.TradeForAsync(tradeId, advance: false, ct);
        var source = await EstimateLinesFor(project, trade).ToListAsync(ct);
        if (source.Count == 0) throw new CrmRuleException("The sold estimate has no material product lines for this trade.");
        var lines = source.Select(l => new OrderSheetLine
        {
            Source = OrderLineSources.Estimate, ProductId = l.ProductId, TakeoffRuleId = l.TakeoffRuleId, Sku = l.Code,
            Description = l.Description, Qty = l.Qty, Unit = l.Unit, UnitCost = l.UnitCost,
        }).ToList();
        await ReplaceAsync(trade, OrderLineSources.Estimate, lines, ct);
        await items.LogAsync(EntityTypes.Trade, tradeId, "order_sheet.estimate", $"Order sheet copied from the estimate: {lines.Count} line{(lines.Count == 1 ? "" : "s")}", ct);
        await db.SaveChangesAsync(ct);
        return lines.Count;
    }

    private async Task ReplaceAsync(ProjectTrade trade, string source, List<OrderSheetLine> lines, CancellationToken ct)
    {
        db.OrderSheetLines.RemoveRange(await db.OrderSheetLines.Where(l => l.ProjectTradeId == trade.Id && l.Source == source).ToListAsync(ct));
        var sort = await db.OrderSheetLines.Where(l => l.ProjectTradeId == trade.Id && l.Source != source).MaxAsync(l => (int?)l.Sort, ct) ?? 0;
        foreach (var l in lines)
        {
            l.ProjectTradeId = trade.Id;
            l.Sort = sort += 10;
            db.OrderSheetLines.Add(l);
        }
    }

    public async Task<Guid> AddLineAsync(Guid tradeId, Guid? productId, string? description, decimal qty, string? unit, string? notes, CancellationToken ct = default)
    {
        var (trade, _) = await projects.TradeForAsync(tradeId, advance: false, ct);
        CheckQty(qty);
        var line = new OrderSheetLine { ProjectTradeId = trade.Id, Source = OrderLineSources.Manual, Qty = qty, Notes = Note(notes) };
        if (productId is { } pid)
        {
            var p = await db.Products.FirstOrDefaultAsync(x => x.Id == pid && x.IsActive, ct) ?? throw new CrmRuleException("Unknown product.");
            (line.ProductId, line.Sku, line.Description, line.Unit, line.UnitCost) = (p.Id, p.Sku, p.Name, p.Unit, p.DefaultCost);
        }
        else
        {
            line.Description = ContactService.Clean(description) ?? throw new CrmRuleException("Pick a product or describe the item.");
            if (line.Description.Length > 500) line.Description = line.Description[..500];
            line.Unit = ContactService.Clean(unit)?.ToUpperInvariant() ?? "EA";
        }
        line.Sort = (await db.OrderSheetLines.Where(l => l.ProjectTradeId == tradeId).MaxAsync(l => (int?)l.Sort, ct) ?? 0) + 10;
        db.OrderSheetLines.Add(line);
        await db.SaveChangesAsync(ct);
        return line.Id;
    }

    public async Task UpdateLineAsync(Guid lineId, decimal qty, string? notes, CancellationToken ct = default)
    {
        var line = await db.OrderSheetLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        await projects.TradeForAsync(line.ProjectTradeId, advance: false, ct);
        CheckQty(qty);
        line.Qty = qty;
        line.Notes = Note(notes);
        // An edited line is the person's now: a refill from its source won't replace it.
        line.Source = OrderLineSources.Manual;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.OrderSheetLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        await projects.TradeForAsync(line.ProjectTradeId, advance: false, ct);
        db.OrderSheetLines.Remove(line);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records that the order was placed (or, with <paramref name="ordered"/> false, that it wasn't after all).</summary>
    public async Task MarkOrderedAsync(Guid tradeId, bool ordered, string? supplier, string? notes, CancellationToken ct = default)
    {
        var (trade, _) = await projects.TradeForAsync(tradeId, advance: false, ct);
        if (ordered)
        {
            if (!await db.OrderSheetLines.AnyAsync(l => l.ProjectTradeId == tradeId, ct)) throw new CrmRuleException("The order sheet is empty.");
            trade.OrderSupplier = ContactService.Clean(supplier) ?? throw new CrmRuleException("Who was it ordered from?");
            trade.OrderNotes = ContactService.Clean(notes);
            trade.OrderedAt = clock.UtcNow;
            trade.OrderedByUserId = scope.Me;
            await items.LogAsync(EntityTypes.Trade, tradeId, "order_sheet.ordered", $"Materials ordered from {trade.OrderSupplier}", ct);
        }
        else
        {
            trade.OrderedAt = null;
            trade.OrderedByUserId = null;
            await items.LogAsync(EntityTypes.Trade, tradeId, "order_sheet.reopened", "Materials order reopened", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static void CheckQty(decimal qty)
    {
        if (qty <= 0) throw new CrmRuleException("Enter a quantity.");
        if (qty > 1_000_000) throw new CrmRuleException("Check the quantity.");
    }

    private static string? Note(string? s) => ContactService.Clean(s) is { } n ? n[..Math.Min(n.Length, 300)] : null;
}
