using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class PushTests(MySqlFixture fx)
{
    private async Task<HttpClient> AppAs(Oes.Infrastructure.Identity.AppUser user)
    {
        var c = fx.App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var res = await c.PostAsJsonAsync("/api/v1/auth/token", new { email = user.Email, password = MySqlFixture.Password });
        var token = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private Task<PushRunResult> Run() => PushService.RunAllAsync(fx.App.Services.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task New_notifications_reach_the_users_phones_once()
    {
        fx.Push.Reset();
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var phone = $"ExponentPushToken[{Guid.NewGuid():N}]";
        var app = await AppAs(rep);

        Assert.Equal((HttpStatusCode)422, (await app.PostAsJsonAsync("/api/v1/devices", new { token = "not-a-token", platform = "ios" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await app.PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "windows" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "iOS" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "ios" })).StatusCode); // again: refresh

        // The admin assigns the rep a task (push), and an automatic bell-only alert and an old one arrive too.
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Call the adjuster", AssignedToUserId = rep.Id });
            var notes = s.Get<NotificationService>();
            await notes.NotifyOnceAsync(rep.Id, $"stale:{Guid.NewGuid()}", "opportunity.stale", "A lead went quiet", null, "/Opportunities");
            notes.Notify(rep.Id, "task.due", "Old reminder", null, "/Tasks");
            await s.Get<OesDbContext>().SaveChangesAsync();
            await s.Get<OesDbContext>().Notifications.Where(n => n.Title == "Old reminder")
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.CreatedAt, DateTime.UtcNow.AddHours(-2)));
        }

        await Run();
        var push = Assert.Single(fx.Push.Sent, m => m.To == phone);
        Assert.Contains("Call the adjuster", push.Title + push.Body);
        Assert.Equal("task.assigned", push.Data["kind"]);
        Assert.StartsWith("/", push.Data["url"]);
        Assert.Equal(3, push.Badge); // the unread count: all three notifications
        await Run();
        Assert.Single(fx.Push.Sent, m => m.To == phone); // never twice

        // Expo says the phone's gone: it's switched off and gets nothing more until it registers again.
        fx.Push.Gone.Add(phone);
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Second task", AssignedToUserId = rep.Id });
        var r = await Run();
        Assert.True(r.Disabled >= 1);
        await using (var s = fx.ScopeAs(t.Id, rep))
            Assert.NotNull((await s.Get<OesDbContext>().DevicePushTokens.SingleAsync(d => d.Token == phone)).DisabledAt);
        fx.Push.Gone.Clear();
        await using (var s = fx.ScopeAs(t.Id, admin))
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Third task", AssignedToUserId = rep.Id });
        await Run();
        Assert.DoesNotContain(fx.Push.Sent, m => m.Title.Contains("Third task") || (m.Body ?? "").Contains("Third task"));

        // Registering again turns it back on; signing out forgets it.
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "ios" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/v1/devices/unregister", new { token = phone })).StatusCode);
        await using (var s = fx.ScopeAs(t.Id, rep))
            Assert.False(await s.Get<OesDbContext>().DevicePushTokens.AnyAsync(d => d.Token == phone));
    }

    [Fact]
    public async Task A_phone_that_changes_hands_moves_to_the_new_user()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var phone = $"ExponentPushToken[{Guid.NewGuid():N}]";
        await (await AppAs(admin)).PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "android" });
        await (await AppAs(rep)).PostAsJsonAsync("/api/v1/devices", new { token = phone, platform = "android" });
        await using var s = fx.ScopeAs(t.Id, admin);
        var row = await s.Get<OesDbContext>().DevicePushTokens.SingleAsync(d => d.Token == phone);
        Assert.Equal((rep.Id, "android"), (row.UserId, row.Platform));
    }
}
