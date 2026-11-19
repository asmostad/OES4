using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Reports;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Reports;

public sealed class SavedReportInput
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RangeKey { get; set; } = ReportRanges.ThisMonth;
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? OwnerFilterId { get; set; }
    public string Schedule { get; set; } = ReportSchedules.None;
    public int ScheduleDay { get; set; }
}

public sealed record SavedReportRow(SavedReport Report, ReportKind Kind, string Url, string RangeText, string? ScheduleText, DateTime? NextSendLocal);

/// <summary>
/// Saved reports: a report kind with its filters and a rolling date range, opened in one click and
/// optionally emailed to its owner weekly or monthly (7 am company time). Everything runs as the
/// owner, so an emailed report never shows more than they could see on screen.
/// </summary>
public sealed class SavedReportService(OesDbContext db, RecordScope scope, ICurrentUser user, TenantTime time, ReportService reports,
    ISystemMailer mailer, IConfiguration config, IClock clock)
{
    public const int MaxPerUser = 20;
    private static readonly string[] Days = CultureInfo.InvariantCulture.DateTimeFormat.DayNames;

    public async Task<List<SavedReportRow>> MineAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var me = scope.Me;
        var list = await db.SavedReports.Where(r => r.UserId == me).OrderBy(r => r.Name).ToListAsync(ct);
        var today = await time.TodayAsync();
        var rows = new List<SavedReportRow>();
        foreach (var r in list)
        {
            var kind = ReportService.Kinds.FirstOrDefault(k => k.Key == r.Kind);
            if (kind is null) continue;
            rows.Add(new SavedReportRow(r, kind, Url(r, today), RangeText(r, kind), ScheduleText(r),
                r.NextSendAt is { } n ? await time.ToLocalAsync(n) : null));
        }
        return rows;
    }

    public async Task<Guid> SaveAsync(SavedReportInput i, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var me = scope.Me ?? throw new RecordForbiddenException("Sign in first.");
        var kind = ReportService.Kinds.FirstOrDefault(k => k.Key == i.Kind) ?? throw new CrmRuleException("Unknown report.");
        var name = (i.Name ?? "").Trim();
        if (name.Length is 0 or > 100) throw new CrmRuleException("Give the report a name (up to 100 characters).");
        if (!ReportRanges.All.Any(r => r.Key == i.RangeKey)) throw new CrmRuleException("Pick a date range.");
        if (i.RangeKey == ReportRanges.Fixed && (i.From is null || i.To is null || i.From > i.To)) throw new CrmRuleException("Pick the dates.");
        // Same rule as the page: only view-all users filter by owner (tasks filter by person for anyone).
        var owner = await user.HasPermissionAsync(Permissions.RecordsViewAll, ct) || kind.Key == "tasks" ? i.OwnerFilterId : null;
        switch (i.Schedule)
        {
            case ReportSchedules.None: break;
            case ReportSchedules.Weekly when i.ScheduleDay is >= 0 and <= 6: break;
            case ReportSchedules.Monthly when i.ScheduleDay is >= 1 and <= 28: break;
            default: throw new CrmRuleException("Pick when to send it.");
        }

        var r = i.Id is { } id
            ? await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct) ?? throw new RecordNotFoundException("Saved report")
            : await db.SavedReports.FirstOrDefaultAsync(x => x.UserId == me && x.Name == name, ct);
        if (r is null)
        {
            if (await db.SavedReports.CountAsync(x => x.UserId == me, ct) >= MaxPerUser) throw new CrmRuleException($"You can keep up to {MaxPerUser} saved reports.");
            r = new SavedReport { UserId = me };
            db.SavedReports.Add(r);
        }
        r.Name = name;
        r.Kind = kind.Key;
        r.RangeKey = kind.UsesRange ? i.RangeKey : ReportRanges.ThisMonth;
        (r.From, r.To) = kind.UsesRange && i.RangeKey == ReportRanges.Fixed ? (i.From, i.To) : (null, null);
        r.OwnerFilterId = owner;
        r.Schedule = i.Schedule;
        r.ScheduleDay = i.Schedule == ReportSchedules.None ? 0 : i.ScheduleDay;
        r.NextSendAt = await NextSendAsync(r.Schedule, r.ScheduleDay, clock.UtcNow);
        r.LastError = null;
        await db.SaveChangesAsync(ct);
        return r.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var me = scope.Me;
        var r = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct) ?? throw new RecordNotFoundException("Saved report");
        db.SavedReports.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Emails the report to its owner now (the signed-in user).</summary>
    public async Task SendNowAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var me = scope.Me;
        var r = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct) ?? throw new RecordNotFoundException("Saved report");
        await SendAsync(r, ct);
        r.LastSentAt = clock.UtcNow;
        r.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Runs the report as the current user (its owner) and emails it to them.</summary>
    internal async Task SendAsync(SavedReport r, CancellationToken ct)
    {
        var kind = ReportService.Kinds.First(k => k.Key == r.Kind);
        var today = await time.TodayAsync();
        var (from, to) = ReportRanges.Resolve(r.RangeKey, today, r.From, r.To);
        var report = await reports.RunAsync(r.Kind, new ReportFilter(from, to, r.OwnerFilterId), ct);
        var email = await db.Users.Where(u => u.Id == r.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct) ?? throw new CrmRuleException("The owner has no email address.");
        var company = await db.Tenants.Where(t => t.Id == r.TenantId).Select(t => t.Name).FirstAsync(ct);
        var link = (config["App:BaseUrl"] ?? "").TrimEnd('/') + Url(r, today);
        var dates = kind.UsesRange ? $"{from:MMM d, yyyy} – {to:MMM d, yyyy}" : $"as of {today:MMM d, yyyy}";
        await mailer.SendAsync(new SystemEmail(email, $"{r.Name} · {company}", Html(company, r.Name, dates, report, link)), ct);
    }

    public static string Url(SavedReport r, DateOnly today)
    {
        var kind = ReportService.Kinds.First(k => k.Key == r.Kind);
        var q = new List<string>();
        if (kind.UsesRange)
        {
            var (from, to) = ReportRanges.Resolve(r.RangeKey, today, r.From, r.To);
            q.Add($"from={from:yyyy-MM-dd}");
            q.Add($"to={to:yyyy-MM-dd}");
        }
        if (r.OwnerFilterId is { } o) q.Add($"owner={o}");
        return $"/Reports/{r.Kind}" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static string RangeText(SavedReport r, ReportKind kind) =>
        !kind.UsesRange ? "As of now"
        : r.RangeKey == ReportRanges.Fixed ? $"{r.From:MMM d, yyyy} – {r.To:MMM d, yyyy}"
        : ReportRanges.All.First(x => x.Key == r.RangeKey).Label;

    private static string? ScheduleText(SavedReport r) => r.Schedule switch
    {
        ReportSchedules.Weekly => $"Emailed every {Days[r.ScheduleDay]}",
        ReportSchedules.Monthly => $"Emailed on the {Ordinal(r.ScheduleDay)} of each month",
        _ => null,
    };

    private static string Ordinal(int n) => n + (n is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>The next send time (7 am company time) strictly after <paramref name="after"/>.</summary>
    internal async Task<DateTime?> NextSendAsync(string schedule, int day, DateTime after)
    {
        if (schedule == ReportSchedules.None) return null;
        var today = DateOnly.FromDateTime(await time.ToLocalAsync(after));
        for (var d = today; d <= today.AddDays(62); d = d.AddDays(1))
        {
            var matches = schedule == ReportSchedules.Weekly ? (int)d.DayOfWeek == day : d.Day == day;
            if (!matches) continue;
            var at = await time.ToUtcAsync(d, new TimeOnly(7, 0));
            if (at > after) return at;
        }
        return null;
    }

    private static string Html(string company, string name, string dates, Report r, string link)
    {
        static string E(object? s) => WebUtility.HtmlEncode(s?.ToString() ?? "");
        static string V(object? v, bool money, bool percent) => v switch
        {
            null => "–",
            decimal d when money => d.ToString("C0", CultureInfo.GetCultureInfo("en-US")),
            decimal d when percent => d.ToString("P0", CultureInfo.InvariantCulture),
            double d when percent => d.ToString("P0", CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#1f2937;max-width:720px\">");
        sb.Append($"<h2 style=\"margin:0\">{E(name)}</h2><div style=\"color:#6b7280;margin-bottom:12px\">{E(r.Title)} · {E(dates)} · {E(company)}</div>");
        sb.Append("<table style=\"border-collapse:collapse;margin-bottom:16px\"><tr>");
        foreach (var k in r.Kpis)
            sb.Append($"<td style=\"padding:8px 14px;border:1px solid #e5e7eb\"><div style=\"font-size:20px;font-weight:600\">{E(V(k.Value, k.Kind == "money", k.Kind == "percent"))}</div><div style=\"color:#6b7280;font-size:12px\">{E(k.Label)}</div></td>");
        sb.Append("</tr></table>");
        foreach (var t in r.Tables)
        {
            sb.Append($"<h3 style=\"font-size:15px;margin:16px 0 6px\">{E(t.Title)}</h3><table style=\"border-collapse:collapse;width:100%;font-size:13px\"><tr>");
            foreach (var c in t.Columns) sb.Append($"<th style=\"text-align:left;border-bottom:2px solid #e5e7eb;padding:4px 6px\">{E(c)}</th>");
            sb.Append("</tr>");
            foreach (var row in t.Rows.Take(50))
            {
                sb.Append("<tr>");
                for (var i = 0; i < row.Length; i++)
                    sb.Append($"<td style=\"border-bottom:1px solid #f3f4f6;padding:4px 6px\">{E(V(row[i], t.MoneyColumns.Contains(i), t.PercentColumns.Contains(i)))}</td>");
                sb.Append("</tr>");
            }
            if (t.Rows.Count > 50) sb.Append($"<tr><td colspan=\"{t.Columns.Length}\" style=\"color:#6b7280;padding:4px 6px\">…and {t.Rows.Count - 50} more</td></tr>");
            if (t.Rows.Count == 0) sb.Append($"<tr><td colspan=\"{t.Columns.Length}\" style=\"color:#6b7280;padding:4px 6px\">Nothing in this range.</td></tr>");
            sb.Append("</table>");
        }
        sb.Append($"<p style=\"margin-top:20px\"><a href=\"{E(link)}\">Open the report in Pich</a></p>");
        sb.Append("<p style=\"color:#9ca3af;font-size:12px\">You saved this report and asked for it by email. Change or stop it on the Reports page.</p></div>");
        return sb.ToString();
    }
}

/// <summary>
/// Sends due scheduled reports (worker). Each runs in its own scope, signed in as its owner for that
/// scope only, so permissions and record visibility are checked exactly as on the page. A report
/// whose owner left or lost reports.view stops being sent.
/// </summary>
public static class SavedReportRunner
{
    public static async Task<int> RunAllAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<Guid> tenants;
        await using (var s = scopes.CreateAsyncScope())
            tenants = await s.ServiceProvider.GetRequiredService<OesDbContext>().Tenants.Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        var sent = 0;
        foreach (var tenantId in tenants)
        {
            List<Guid> due;
            await using (var s = scopes.CreateAsyncScope())
            {
                s.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
                var now = s.ServiceProvider.GetRequiredService<IClock>().UtcNow;
                due = await s.ServiceProvider.GetRequiredService<OesDbContext>().SavedReports.Where(r => r.NextSendAt != null && r.NextSendAt <= now)
                    .Select(r => r.Id).Take(200).ToListAsync(ct);
            }
            foreach (var id in due)
                if (await SendOneAsync(scopes, tenantId, id, ct)) sent++;
        }
        return sent;
    }

    private static async Task<bool> SendOneAsync(IServiceScopeFactory scopes, Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var s = scopes.CreateAsyncScope();
        var sp = s.ServiceProvider;
        sp.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = sp.GetRequiredService<OesDbContext>();
        var r = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        var now = sp.GetRequiredService<IClock>().UtcNow;
        // Act as the owner for this scope only.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, r.UserId.ToString())], "scheduled-report")),
            RequestServices = sp,
        };
        var service = sp.GetRequiredService<SavedReportService>();
        try
        {
            if (!await sp.GetRequiredService<ICurrentUser>().HasPermissionAsync(Permissions.ReportsView, ct))
            {
                r.Schedule = ReportSchedules.None;
                r.NextSendAt = null;
                r.LastError = "Stopped: you no longer have access to reports here.";
                await db.SaveChangesAsync(ct);
                return false;
            }
            await service.SendAsync(r, ct);
            r.LastSentAt = now;
            r.LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            r.LastError = ex is CrmRuleException or RecordForbiddenException ? ex.Message : "Couldn't send it; it will try again next time.";
            sp.GetRequiredService<ILogger<SavedReportService>>().LogError(ex, "Scheduled report {ReportId} failed", id);
            return false;
        }
        finally
        {
            if (r.Schedule != ReportSchedules.None) r.NextSendAt = await service.NextSendAsync(r.Schedule, r.ScheduleDay, now);
            await db.SaveChangesAsync(ct);
        }
    }
}
