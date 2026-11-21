using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;
using Oes.Domain.Ai;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Ai;

/// <summary>Daily AI spend per user and company: checked before every model call, recorded after it. Shared by every AI feature.</summary>
public sealed class AiUsageMeter(OesDbContext db, IOptions<AiOptions> options)
{
    private readonly AiOptions _o = options.Value;

    /// <summary>Null when the user may make another model call; otherwise why not.</summary>
    public async Task<string?> OverBudgetAsync(Guid userId, DateOnly today, CancellationToken ct = default)
    {
        var mine = await db.AiUsageDaily.Where(u => u.UserId == userId && u.Date == today).Select(u => (decimal?)u.CostUsd).FirstOrDefaultAsync(ct) ?? 0;
        if (mine >= _o.DailyUserBudgetUsd) return "You've reached today's assistant limit. It resets tomorrow.";
        var company = await db.AiUsageDaily.Where(u => u.Date == today).SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0;
        if (company >= _o.DailyTenantBudgetUsd) return "Your company has reached today's assistant limit. It resets tomorrow.";
        return null;
    }

    /// <summary>Adds a call's usage to today's row and returns its cost. Saved with the caller's SaveChanges.</summary>
    public async Task<decimal> RecordAsync(Guid userId, DateOnly today, AiUsage u, CancellationToken ct = default)
    {
        var row = db.AiUsageDaily.Local.FirstOrDefault(x => x.UserId == userId && x.Date == today)
                  ?? await db.AiUsageDaily.FirstOrDefaultAsync(x => x.UserId == userId && x.Date == today, ct);
        if (row is null) db.AiUsageDaily.Add(row = new AiUsageDaily { UserId = userId, Date = today });
        var cost = _o.Cost(u);
        row.InputTokens += u.InputTokens; row.OutputTokens += u.OutputTokens; row.CacheReadTokens += u.CacheReadTokens;
        row.CacheWriteTokens += u.CacheWriteTokens; row.Requests++; row.CostUsd += cost;
        return cost;
    }
}
