using System.Text;
using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Reports;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Reports;

[HasPermission(Permissions.ReportsView)]
public class IndexModel(ReportService reports, SavedReportService saved, CrmLookups lookups, TenantTime time, ICurrentUser user) : CrmPageModel
{
    public static IReadOnlyList<ReportKind> Kinds => ReportService.Kinds;

    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Owner { get; set; }
    [BindProperty(SupportsGet = true)] public string? Format { get; set; }

    public Report? Result { get; private set; }
    public ReportKind Current { get; private set; } = null!;
    public List<Option> Users { get; private set; } = [];
    public bool CanPickOwner { get; private set; }
    public List<SavedReportRow> Saved { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Kind is null) { Saved = await saved.MineAsync(); return Page(); }
        if (Kinds.FirstOrDefault(k => k.Key == Kind) is not { } kind) return NotFound();
        Current = kind;

        var today = await time.TodayAsync();
        To ??= today;
        From ??= new DateOnly(today.Year, 1, 1);
        if (From > To) (From, To) = (To, From);
        if (To.Value.DayNumber - From.Value.DayNumber > 3 * 366) From = To.Value.AddYears(-3);
        CanPickOwner = await user.HasPermissionAsync(Permissions.RecordsViewAll);
        if (CanPickOwner) Users = await lookups.UsersAsync();
        var filter = new ReportFilter(From.Value, To.Value, CanPickOwner || Kind == "tasks" ? Owner : null);

        Result = await reports.RunAsync(Kind, filter);
        return Format == "csv" ? Csv(Result) : Page();
    });

    /// <summary>Saves the report on screen. Weekly and monthly days come from separate fields.</summary>
    public Task<IActionResult> OnPostSaveAsync(string name, string rangeKey, string schedule, int weeklyDay, int monthlyDay) => Guard(async () =>
    {
        await saved.SaveAsync(new SavedReportInput
        {
            Name = name, Kind = Kind ?? "", RangeKey = rangeKey, From = From, To = To, OwnerFilterId = Owner, Schedule = schedule,
            ScheduleDay = schedule == Oes.Domain.Reports.ReportSchedules.Monthly ? monthlyDay : weeklyDay,
        });
        Flash(schedule == Oes.Domain.Reports.ReportSchedules.None ? "Report saved." : "Report saved. It will be emailed to you on schedule.");
        return RedirectToPage(new { kind = (string?)null });
    }, () =>
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(new { kind = Kind, from = From, to = To, owner = Owner });
    });

    public Task<IActionResult> OnPostDeleteAsync(Guid id) => Guard(async () =>
    {
        await saved.DeleteAsync(id);
        Flash("Saved report deleted.");
        return RedirectToPage(new { kind = (string?)null });
    });

    public Task<IActionResult> OnPostSendAsync(Guid id) => Guard(async () =>
    {
        await saved.SendNowAsync(id);
        Flash("Sent to your email.");
        return RedirectToPage(new { kind = (string?)null });
    });

    private FileContentResult Csv(Report r)
    {
        var sb = new StringBuilder();
        CsvWriter.Row(sb, [r.Title + (Current.UsesRange ? $" {From:yyyy-MM-dd} to {To:yyyy-MM-dd}" : "")]);
        foreach (var k in r.Kpis) CsvWriter.Row(sb, [k.Label, k.Value]);
        foreach (var t in r.Tables)
        {
            sb.Append("\r\n");
            CsvWriter.Row(sb, [t.Title]);
            CsvWriter.Row(sb, t.Columns);
            foreach (var row in t.Rows) CsvWriter.Row(sb, row);
            if (t.Totals is not null) CsvWriter.Row(sb, t.Totals);
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(); // BOM so Excel reads UTF-8
        return File(bytes, "text/csv", $"{Current.Key}-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
