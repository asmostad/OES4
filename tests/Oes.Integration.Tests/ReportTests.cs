using System.Text;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Admin;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Reports;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ReportTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser RepA, AppUser RepB, DateOnly Today);

    /// <summary>
    /// Rep A: open 1,000 and 2,000 (one stale), won 3,000. Rep B: open 5,000, lost (price).
    /// Both reps can see reports but not other people's records.
    /// </summary>
    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<TenantAdminService>().SaveRoleAsync(null, "Reporting Rep", null,
                [Permissions.RecordsCreate, Permissions.RecordsEditOwn, Permissions.TasksCreate, Permissions.ReportsView]);
        var a = await fx.AddMemberAsync(t.Id, "Reporting Rep");
        var b = await fx.AddMemberAsync(t.Id, "Reporting Rep");
        DateOnly today;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            async Task<Guid> Opp(AppUser owner, decimal value, string source)
            {
                var c = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "R", LastName = $"{value}", OwnerUserId = owner.Id });
                return await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = c, OwnerUserId = owner.Id, ExpectedValue = value, SourceKey = source });
            }
            var stale = await Opp(a, 1000, "referral");
            await Opp(a, 2000, "door_knock");
            var won = await Opp(a, 3000, "referral");
            await Opp(b, 5000, "door_knock");
            var lost = await Opp(b, 9999, "website");
            var now = DateTime.UtcNow;
            await db.Opportunities.Where(o => o.Id == won).ExecuteUpdateAsync(u => u.SetProperty(o => o.Outcome, Outcomes.Won).SetProperty(o => o.WonAt, now).SetProperty(o => o.ClosedAt, now));
            await db.Opportunities.Where(o => o.Id == lost).ExecuteUpdateAsync(u => u.SetProperty(o => o.Outcome, Outcomes.Lost).SetProperty(o => o.ClosedAt, now).SetProperty(o => o.LostReasonKey, "price"));
            var stage = await db.Opportunities.Where(o => o.Id == stale).Select(o => o.StageId).SingleAsync();
            await db.PipelineStages.Where(p => p.Id == stage).ExecuteUpdateAsync(u => u.SetProperty(p => p.StaleAfterHours, 48));
            await db.Opportunities.Where(o => o.Id == stale).ExecuteUpdateAsync(u => u.SetProperty(o => o.StageChangedAt, now.AddDays(-5)));
            today = await s.Get<TenantTime>().TodayAsync();
        }
        await using (var s = fx.ScopeAs(t.Id, a))
        {
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Overdue", DueDate = today.AddDays(-2) });
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Later", DueDate = today.AddDays(5) });
        }
        await using (var s = fx.ScopeAs(t.Id, b)) await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "B's" });
        return new World(t.Id, admin, a, b, today);
    }

    private static Kpi K(Report r, string label) => r.Kpis.Single(k => k.Label == label);

    [Fact]
    public async Task Pipeline_and_sales_count_only_records_the_user_can_see()
    {
        var w = await WorldAsync();
        var range = new ReportFilter(w.Today.AddDays(-1), w.Today);
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var reports = s.Get<ReportService>();
            var pipe = await reports.PipelineAsync(range);
            Assert.Equal((3, 8000m), ((int)K(pipe, "Open opportunities").Value, (decimal)K(pipe, "Expected value").Value));
            Assert.Equal(8000m, pipe.Tables[0].Totals![2]);

            var sales = await reports.SalesAsync(range);
            Assert.Equal((5, 1, 0.5, 3000m), ((int)K(sales, "New leads").Value, (int)K(sales, "Won").Value, (double)K(sales, "Win rate").Value, (decimal)K(sales, "Won value").Value));
            var referral = sales.Tables.Single(t => t.Title == "By lead source").Rows.Single(r => (string)r[0]! == "Referral");
            Assert.Equal((2, 1), ((int)referral[1]!, (int)referral[2]!));
            Assert.Equal("Price", sales.Tables.Single(t => t.Title == "Why we lost").Rows.Single()[0]);

            var onlyB = await reports.PipelineAsync(range with { OwnerId = w.RepB.Id });
            Assert.Equal(5000m, (decimal)K(onlyB, "Expected value").Value);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.RepA))
        {
            var reports = s.Get<ReportService>();
            var pipe = await reports.PipelineAsync(range);
            Assert.Equal((2, 3000m), ((int)K(pipe, "Open opportunities").Value, (decimal)K(pipe, "Expected value").Value)); // B's are hidden
            var sales = await reports.SalesAsync(range);
            Assert.Equal((3, 0), ((int)K(sales, "New leads").Value, sales.Tables.Single(t => t.Title == "Why we lost").Rows.Count));
            // Asking for someone else's records through the owner filter still returns nothing.
            Assert.Equal(0, (int)K(await reports.PipelineAsync(range with { OwnerId = w.RepB.Id }), "Open opportunities").Value);
        }
    }

    [Fact]
    public async Task Stale_and_task_reports()
    {
        var w = await WorldAsync();
        var range = new ReportFilter(w.Today.AddDays(-7), w.Today);
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var stale = await s.Get<ReportService>().StaleAsync(range);
            var row = Assert.Single(stale.Tables[0].Rows);
            Assert.Equal(5, (int)row[3]!);
            Assert.StartsWith("/Opportunities/View/", stale.Tables[0].Links[0]);

            var tasks = await s.Get<ReportService>().TasksAsync(range); // admin has tasks.view_all
            Assert.Equal((3, 1), ((int)K(tasks, "Open").Value, (int)K(tasks, "Overdue").Value));
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.RepB))
        {
            var tasks = await s.Get<ReportService>().TasksAsync(range);
            Assert.Equal((1, 0), ((int)K(tasks, "Open").Value, (int)K(tasks, "Overdue").Value)); // only their own
        }
        var plain = await fx.AddMemberAsync(w.Tenant, "Sales Rep"); // no reports.view
        await using (var s = fx.ScopeAs(w.Tenant, plain))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<ReportService>().PipelineAsync(range));
    }

    [Fact]
    public void Csv_cells_are_quoted_and_cannot_become_formulas()
    {
        var sb = new StringBuilder();
        CsvWriter.Row(sb, ["=HYPERLINK(\"http://x\")", "Smith, Jane", "+1 402", 1234.5m, 0.25, "@SUM(A1)", "plain"]);
        Assert.Equal("\"'=HYPERLINK(\"\"http://x\"\")\",\"Smith, Jane\",'+1 402,1234.50,0.25,'@SUM(A1),plain\r\n", sb.ToString());
    }
}
