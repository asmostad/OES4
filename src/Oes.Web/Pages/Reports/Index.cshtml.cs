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
public class IndexModel(ReportService reports, CrmLookups lookups, TenantTime time, ICurrentUser user) : CrmPageModel
{
    public static readonly (string Key, string Title, string Icon, string Blurb, bool UsesRange)[] Kinds =
    [
        ("pipeline", "Pipeline", "bi-funnel", "Open opportunities by stage and owner, right now.", false),
        ("sales", "Sales", "bi-trophy", "New leads, wins, losses and win rate by month, source and owner.", true),
        ("production", "Production", "bi-kanban", "Active projects and trades by stage, backlog, sold and completed.", true),
        ("stale", "Stale", "bi-hourglass-split", "Opportunities and projects stuck in a stage too long.", false),
        ("tasks", "Tasks", "bi-check2-square", "Open, overdue and finished tasks by person.", true),
        ("calls", "Prospecting calls", "bi-telephone-outbound", "Call outcomes by outcome and by person.", true),
        ("marketing", "Marketing", "bi-megaphone", "Email blast results and the leads they brought in, and nurture workflows.", true),
    ];

    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Owner { get; set; }
    [BindProperty(SupportsGet = true)] public string? Format { get; set; }

    public Report? Result { get; private set; }
    public (string Key, string Title, string Icon, string Blurb, bool UsesRange) Current { get; private set; }
    public List<Option> Users { get; private set; } = [];
    public bool CanPickOwner { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Kind is null) return Page();
        Current = Kinds.FirstOrDefault(k => k.Key == Kind);
        if (Current.Key is null) return NotFound();

        var today = await time.TodayAsync();
        To ??= today;
        From ??= new DateOnly(today.Year, 1, 1);
        if (From > To) (From, To) = (To, From);
        if (To.Value.DayNumber - From.Value.DayNumber > 3 * 366) From = To.Value.AddYears(-3);
        CanPickOwner = await user.HasPermissionAsync(Permissions.RecordsViewAll);
        if (CanPickOwner) Users = await lookups.UsersAsync();
        var filter = new ReportFilter(From.Value, To.Value, CanPickOwner || Kind == "tasks" ? Owner : null);

        Result = Kind switch
        {
            "pipeline" => await reports.PipelineAsync(filter),
            "sales" => await reports.SalesAsync(filter),
            "production" => await reports.ProductionAsync(filter),
            "stale" => await reports.StaleAsync(filter),
            "calls" => await reports.CallsAsync(filter),
            "marketing" => await reports.MarketingAsync(filter),
            _ => await reports.TasksAsync(filter),
        };
        return Format == "csv" ? Csv(Result) : Page();
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
