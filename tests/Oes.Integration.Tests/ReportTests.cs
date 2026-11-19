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

    [Fact]
    public async Task Marketing_report_counts_blast_results_and_nurture_runs()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var db = s.Get<OesDbContext>();
        var contacts = s.Get<ContactService>();
        var a = await contacts.CreateAsync(new ContactInput { FirstName = "Bea", LastName = "Blast", Methods = [new("email", "bea@example.test", null, true)] });
        var b = await contacts.CreateAsync(new ContactInput { FirstName = "Cy", LastName = "Blast", Methods = [new("email", "cy@example.test", null, true)] });
        var c = await contacts.CreateAsync(new ContactInput { FirstName = "Di", LastName = "Blast" });
        var now = DateTime.UtcNow;
        var blast = new Oes.Domain.Communication.Blast { Name = "Spring tune-up", Subject = "Hi", Body = "Hi", SenderUserId = admin.Id,
            Status = Oes.Domain.Communication.BlastStatuses.Sent, StartedAt = now.AddHours(-2), FinishedAt = now.AddHours(-1), Total = 3 };
        db.Blasts.Add(blast);
        db.BlastRecipients.AddRange(
            new Oes.Domain.Communication.BlastRecipient { BlastId = blast.Id, ContactId = a, Address = "bea@example.test", Status = Oes.Domain.Communication.BlastRecipientStatuses.Sent },
            new Oes.Domain.Communication.BlastRecipient { BlastId = blast.Id, ContactId = b, Address = "cy@example.test", Status = Oes.Domain.Communication.BlastRecipientStatuses.Sent },
            new Oes.Domain.Communication.BlastRecipient { BlastId = blast.Id, ContactId = c, Address = "old@example.test", Status = Oes.Domain.Communication.BlastRecipientStatuses.Failed });
        db.Suppressions.Add(new Oes.Domain.Communication.Suppression { Channel = "email", Address = "cy@example.test", Reason = "unsubscribe", Scope = "marketing" });
        var seq = new Oes.Domain.Communication.Sequence { Name = "New lead follow-up" };
        db.Sequences.Add(seq);
        await db.SaveChangesAsync();
        var lead = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = a, OwnerUserId = admin.Id });
        db.Enrollments.AddRange(
            new Oes.Domain.Communication.Enrollment { SequenceId = seq.Id, ContactId = a, OpportunityId = lead },
            new Oes.Domain.Communication.Enrollment { SequenceId = seq.Id, ContactId = b, Status = Oes.Domain.Communication.EnrollmentStatuses.Stopped, StopReason = "replied" });
        await db.SaveChangesAsync();
        var won = (await s.Get<OpportunityService>().StagesAsync()).Single(x => x.Outcome == Outcomes.Won);
        await s.Get<OpportunityService>().MoveStageAsync(lead, won.Id, null);

        var today = await s.Get<TenantTime>().TodayAsync();
        var r = await s.Get<ReportService>().MarketingAsync(new ReportFilter(today.AddDays(-1), today));
        Assert.Equal([2, 1, 1, 2], r.Kpis.Select(k => (int)k.Value));
        var row = Assert.Single(r.Tables[0].Rows);
        Assert.Equal(["Spring tune-up", 2, 1, 0, 1, 1, 1], new[] { row[0], row[2], row[3], row[4], row[5], row[6], row[7] });
        Assert.Equal(["New lead follow-up", 2, 1, 0, 1, 1], r.Tables[1].Rows.Single());
        Assert.Equal(["Replied", 1], r.Tables[2].Rows.Single());
    }

    [Fact]
    public async Task Calls_report_counts_outcomes_by_person()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var p = s.Get<ProspectingService>();
        var outcomes = (await p.DispositionsAsync()).ToDictionary(d => d.Name);
        async Task<Guid> Lead(string n) => await s.Get<OpportunityService>().CreateAsync(new OpportunityInput
            { PrimaryContactId = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = n, LastName = "Call" }) });
        var x = await Lead("Xo");
        var y = await Lead("Yu");
        await p.ApplyAsync(x, outcomes["No answer"].Id, null);
        await p.ApplyAsync(x, outcomes["Inspection set"].Id, null);
        await p.ApplyAsync(y, outcomes["Do not contact"].Id, null);

        var today = await s.Get<TenantTime>().TodayAsync();
        var r = await s.Get<ReportService>().CallsAsync(new ReportFilter(today, today));
        Assert.Equal([3, 2, 1, 1], r.Kpis.Select(k => (int)k.Value));
        Assert.Equal(["No answer", "Inspection set", "Do not contact"], r.Tables[0].Rows.Select(row => (string)row[0]!));
        Assert.Equal([3, 2, 1, 1], r.Tables[1].Rows.Single().Skip(1).Select(v => (int)v!));

        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await web.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await web.GetAsync("/Account/SelectTenant");
        Assert.Contains("Calls by person", await web.GetStringAsync("/Reports?kind=calls"));
        Assert.Contains("Nurture workflows", await web.GetStringAsync("/Reports?kind=marketing"));
        Assert.Contains("Inspection set", await web.GetStringAsync("/Reports?kind=calls&format=csv"));
    }
}
