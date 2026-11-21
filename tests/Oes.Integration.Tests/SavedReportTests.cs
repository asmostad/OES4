using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Reports;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Reports;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class SavedReportTests(MySqlFixture fx)
{
    private string[] Mail(string marker) =>
        Directory.Exists(Path.Combine(fx.FilesRoot, "mail"))
            ? Directory.GetFiles(Path.Combine(fx.FilesRoot, "mail")).Select(File.ReadAllText).Where(h => h.Contains(marker)).ToArray()
            : [];

    [Fact]
    public async Task Saved_reports_roll_their_dates_and_are_emailed_to_their_owner_on_schedule()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var manager = await fx.AddMemberAsync(t.Id, "Sales Manager");
        var marker = "Weekly sales " + Guid.NewGuid().ToString("N")[..6];
        Guid id;
        DateOnly today;
        await using (var s = fx.ScopeAs(t.Id, manager))
        {
            var saved = s.Get<SavedReportService>();
            today = await s.Get<TenantTime>().TodayAsync();
            await Assert.ThrowsAsync<CrmRuleException>(() => saved.SaveAsync(new SavedReportInput { Name = "X", Kind = "nope" }));
            await Assert.ThrowsAsync<CrmRuleException>(() => saved.SaveAsync(new SavedReportInput { Name = "X", Kind = "sales", RangeKey = ReportRanges.Fixed }));
            await Assert.ThrowsAsync<CrmRuleException>(() => saved.SaveAsync(new SavedReportInput { Name = "X", Kind = "sales", Schedule = ReportSchedules.Monthly, ScheduleDay = 30 }));

            id = await saved.SaveAsync(new SavedReportInput { Name = marker, Kind = "sales", RangeKey = ReportRanges.Last30, Schedule = ReportSchedules.Weekly, ScheduleDay = 1 });
            Assert.Equal(id, await saved.SaveAsync(new SavedReportInput { Name = marker, Kind = "sales", RangeKey = ReportRanges.ThisMonth, Schedule = ReportSchedules.Weekly, ScheduleDay = 1 })); // same name: replaced
            var row = Assert.Single(await saved.MineAsync());
            Assert.Equal($"/Reports/sales?from={new DateOnly(today.Year, today.Month, 1):yyyy-MM-dd}&to={today:yyyy-MM-dd}", row.Url);
            Assert.Equal(("This month", "Emailed every Monday"), (row.RangeText, row.ScheduleText));
            Assert.Equal((DayOfWeek.Monday, 7), (row.NextSendLocal!.Value.DayOfWeek, row.NextSendLocal.Value.Hour)); // 7 am company time

            await saved.SendNowAsync(id);
            Assert.Single(Mail(marker)); // to the manager, with the KPIs
            Assert.Contains("New leads", Mail(marker)[0]);
        }

        // Someone else can't touch it.
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<SavedReportService>().DeleteAsync(id));
            Assert.Empty(await s.Get<SavedReportService>().MineAsync());
        }

        // The worker sends it when due, as its owner, and moves the next send on a week.
        var scopes = fx.App.Services.GetRequiredService<IServiceScopeFactory>();
        async Task MakeDue() { await using var s = fx.ScopeFor(t.Id); await s.Get<OesDbContext>().SavedReports.Where(r => r.Id == id).ExecuteUpdateAsync(x => x.SetProperty(r => r.NextSendAt, DateTime.UtcNow.AddMinutes(-1))); }
        async Task<SavedReport> Load() { await using var s = fx.ScopeFor(t.Id); return await s.Get<OesDbContext>().SavedReports.SingleAsync(r => r.Id == id); }
        await MakeDue();
        Assert.True(await SavedReportRunner.RunAllAsync(scopes) >= 1);
        Assert.Equal(2, Mail(marker).Length);
        var after = await Load();
        Assert.True(after.NextSendAt > DateTime.UtcNow && after.NextSendAt < DateTime.UtcNow.AddDays(8)); // the coming Monday, 7 am
        Assert.NotNull(after.LastSentAt);

        // An owner who leaves stops getting it.
        await using (var s = fx.ScopeFor(t.Id))
            await s.Get<OesDbContext>().Memberships.Where(m => m.UserId == manager.Id).ExecuteUpdateAsync(x => x.SetProperty(m => m.Status, MembershipStatus.Disabled));
        await MakeDue();
        await SavedReportRunner.RunAllAsync(scopes);
        var stopped = await Load();
        Assert.Equal((ReportSchedules.None, (DateTime?)null), (stopped.Schedule, stopped.NextSendAt));
        Assert.Contains("no longer have access", stopped.LastError);
        Assert.Equal(2, Mail(marker).Length);
    }

    [Fact]
    public async Task The_reports_page_saves_lists_and_deletes()
    {
        var (_, admin) = await fx.CreateTenantAsync();
        var web = fx.App.Browser();
        string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")) }));
        await web.GetAsync("/Account/SelectTenant");

        var page = await web.GetStringAsync("/Reports/tasks?from=2026-01-01&to=2026-01-31");
        Assert.Contains("Save this report", page);
        var save = await web.PostAsync("/Reports/tasks?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "January tasks", ["rangeKey"] = ReportRanges.Fixed, ["schedule"] = "monthly", ["weeklyDay"] = "1", ["monthlyDay"] = "3",
            ["from"] = "2026-01-01", ["to"] = "2026-01-31", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var list = await web.GetStringAsync("/Reports");
        Assert.Contains("January tasks", list);
        Assert.Contains("Emailed on the 3rd of each month", list);
        Assert.Contains("/Reports/tasks?from=2026-01-01&amp;to=2026-01-31", list);
    }
}
