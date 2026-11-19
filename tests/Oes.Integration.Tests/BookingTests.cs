using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Booking;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Work;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Scheduling;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed partial class BookingTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, string Slug, AppUser Admin, AppUser Rep, DateOnly Day);

    [GeneratedRegex(@"/portal/b/([A-Za-z0-9_\-]+)")]
    private static partial Regex ManageLink();

    private static readonly TimeOnly Nine = new(9, 0), Ten = new(10, 0), Eleven = new(11, 0);

    /// <summary>Admin and rep both take "Roof inspection" (60 min, hourly, 30 min after), 9–12 every day.</summary>
    private async Task<World> WorldAsync(int? cap = null)
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var everyDay = Enum.GetValues<DayOfWeek>().Select(d => (d, Nine, new TimeOnly(12, 0))).ToList();
        DateOnly day;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var b = s.Get<BookingService>();
            await b.SaveTypeAsync(null, new BookingTypeInput
            {
                Name = "Roof inspection", DurationMinutes = 60, SlotIntervalMinutes = 60, BufferBeforeMinutes = 0, BufferAfterMinutes = 30,
                MinNoticeMinutes = 0, DaysAhead = 14, DailyCapPerHost = cap, HostUserIds = [admin.Id, rep.Id],
            });
            await b.SaveHoursAsync(admin.Id, everyDay);
            day = (await s.Get<TenantTime>().TodayAsync()).AddDays(1);
        }
        await using (var s = fx.ScopeAs(t.Id, rep)) await s.Get<BookingService>().SaveHoursAsync(rep.Id, everyDay);
        return new World(t.Id, t.Slug, admin, rep, day);
    }

    private static BookingRequest Request(DateOnly day, TimeOnly time, string first = "Pat", string email = "pat@example.com", string phone = "(402) 555-0142") => new()
    {
        Date = day, Time = time, FirstName = first, LastName = "Booker", Email = email, Phone = phone,
        Line1 = "77 Booking Blvd", City = "Lincoln", State = "ne", PostalCode = "68502", Notes = "Leak over the garage", TextConsent = true,
    };

    /// <summary>A public scope: no signed-in user, company from the booking URL.</summary>
    private async Task<AsyncServiceScopeHolder> PublicAsync(World w)
    {
        var s = fx.ScopeFor(null);
        Assert.NotNull(await s.Get<BookingService>().OpenCompanyAsync(w.Slug));
        return new AsyncServiceScopeHolder(s);
    }

    private sealed class AsyncServiceScopeHolder(Microsoft.Extensions.DependencyInjection.AsyncServiceScope scope) : IAsyncDisposable
    {
        public T Get<T>() where T : notnull => scope.Get<T>();
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    [Fact]
    public async Task Slots_respect_hours_calendars_buffers_round_robin_and_daily_caps()
    {
        var w = await WorldAsync(cap: 1);
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) // admin is busy 10–11 tomorrow
            await s.Get<WorkService>().CreateAppointmentAsync(new AppointmentInput { Title = "Adjuster", Date = w.Day, StartTime = Ten, DurationMinutes = 60 });

        await using (var p = await PublicAsync(w))
        {
            var b = p.Get<BookingService>();
            Assert.Equal([Nine, Ten, Eleven], (await b.SlotsAsync("roof-inspection"))[w.Day]);
            var first = await b.BookAsync("roof-inspection", Request(w.Day, Ten), "203.0.113.5");
            Assert.Contains("Member", first.HostName); // admin was busy, so the rep took it
            Assert.Contains("isn't available", (await Assert.ThrowsAsync<CrmRuleException>(() => b.BookAsync("roof-inspection", Request(w.Day, Ten), null))).Message);
            var second = await b.BookAsync("roof-inspection", Request(w.Day, Eleven, "Sam", "sam@example.com", "(402) 555-0143"), null);
            Assert.DoesNotContain("Member", second.HostName); // fewest upcoming bookings: admin
            // Each host has hit the one-a-day cap: tomorrow is full.
            Assert.False((await b.SlotsAsync("roof-inspection")).ContainsKey(w.Day));
            await Assert.ThrowsAsync<CrmRuleException>(() => b.BookAsync("roof-inspection", Request(w.Day, Nine), null));
        }
    }

    [Fact]
    public async Task Invitations_and_all_day_events_block_booking_times()
    {
        var w = await WorldAsync();
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var work = s.Get<WorkService>();
            // The admin is off all day and the rep is invited to the admin's 9 o'clock ride-along: only the rep's 10 and 11 are left.
            await work.CreateAppointmentAsync(new AppointmentInput { Title = "Day off", AllDay = true, Date = w.Day });
            await work.CreateAppointmentAsync(new AppointmentInput { Title = "Ride-along", Date = w.Day, StartTime = Nine, DurationMinutes = 60, AttendeeUserIds = [w.Rep.Id] });
        }
        await using var p = await PublicAsync(w);
        Assert.Equal([Ten, Eleven], (await p.Get<BookingService>().SlotsAsync("roof-inspection"))[w.Day]);
    }

    [Fact]
    public async Task Outside_calendar_feeds_block_booking_times_and_keep_only_when()
    {
        var w = await WorldAsync();
        var url = $"https://calendar.example.com/private/{Guid.NewGuid():N}/basic.ics";
        DateTime StartUtc(TimeOnly t) => TimeZoneInfo.ConvertTimeToUtc(w.Day.ToDateTime(t), TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
        string Event(TimeOnly t, string summary) => $"BEGIN:VEVENT\r\nDTSTART:{StartUtc(t):yyyyMMdd'T'HHmmss}Z\r\nDURATION:PT1H\r\nSUMMARY:{summary}\r\nEND:VEVENT\r\n";
        fx.Feeds.Set(url, "BEGIN:VCALENDAR\r\n" + Event(Nine, "Kids pickup") + "END:VCALENDAR\r\n");

        Guid cal;
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var calendars = s.Get<BusyCalendarService>();
            await Assert.ThrowsAsync<CrmRuleException>(() => calendars.AddAsync(w.Rep.Id, null, "http://calendar.example.com/a.ics"));
            await Assert.ThrowsAsync<CrmRuleException>(() => calendars.AddAsync(w.Rep.Id, null, "https://169.254.169.254/latest/meta-data"));
            await Assert.ThrowsAsync<CrmRuleException>(() => calendars.AddAsync(w.Rep.Id, null, "https://calendar.example.com/unknown.ics")); // unreachable: not saved
            await Assert.ThrowsAsync<RecordForbiddenException>(() => calendars.AddAsync(w.Admin.Id, null, url)); // someone else's
            cal = await calendars.AddAsync(w.Rep.Id, "Personal", url.Replace("https:", "webcal:"));
            var row = Assert.Single(await calendars.ForUserAsync(w.Rep.Id));
            Assert.Equal(("Personal", "calendar.example.com", 1, (string?)null), (row.Calendar.Label, row.Calendar.Host, row.Calendar.BlockCount, row.Calendar.LastError));
            var db = s.Get<OesDbContext>();
            Assert.DoesNotContain(url, (await db.BusyCalendars.SingleAsync(c => c.Id == cal)).UrlProtected); // encrypted at rest
            Assert.Equal(StartUtc(Nine), (await db.BusyBlocks.SingleAsync(b => b.BusyCalendarId == cal)).StartsAt);
        }

        // Admin is busy all morning; the rep's feed blocks 9, so nobody can take 9 but 10 and 11 are open.
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
            await s.Get<WorkService>().CreateAppointmentAsync(new AppointmentInput { Title = "Morning", Date = w.Day, StartTime = Nine, DurationMinutes = 180 });
        await using (var p = await PublicAsync(w))
            Assert.Equal([Ten, Eleven], (await p.Get<BookingService>().SlotsAsync("roof-inspection"))[w.Day]);

        // The feed changes; the worker picks it up. A failed read keeps the last busy times.
        fx.Feeds.Set(url, "BEGIN:VCALENDAR\r\n" + Event(new TimeOnly(7, 0), "Gym") + Event(Eleven, "Lunch") + "END:VCALENDAR\r\n"); // a 10 o'clock would also block 9 (30-minute buffer)
        await using (var s = fx.ScopeFor(w.Tenant))
            await s.Get<OesDbContext>().BusyCalendars.Where(c => c.Id == cal).ExecuteUpdateAsync(x => x.SetProperty(c => c.LastFetchedAt, DateTime.UtcNow.AddHours(-1)));
        Assert.True(await BusyCalendarRunner.RefreshAllAsync(fx.App.Services.GetRequiredService<IServiceScopeFactory>()) >= 1);
        await using (var p = await PublicAsync(w))
            Assert.Equal([Nine], (await p.Get<BookingService>().SlotsAsync("roof-inspection"))[w.Day]);

        fx.Feeds.Set(url, "<html>Not a calendar</html>");
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            await s.Get<BusyCalendarService>().RefreshNowAsync(cal);
            var row = Assert.Single(await s.Get<BusyCalendarService>().ForUserAsync(w.Rep.Id));
            Assert.Equal((2, "That link isn't an .ics calendar feed."), (row.Calendar.BlockCount, row.Calendar.LastError));
            Assert.Equal(2, await s.Get<OesDbContext>().BusyBlocks.CountAsync(b => b.BusyCalendarId == cal));

            // The rep sees their busy times on the OES calendar (no event details), until they hide them.
            Assert.Equal(2, (await s.Get<BusyCalendarService>().BlocksAsync(w.Day, w.Day, null)).Count);
            var web = fx.App.Browser();
            await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = w.Rep.Email!, ["Password"] = MySqlFixture.Password,
                ["__RequestVerificationToken"] = Regex.Match(await web.GetStringAsync("/Account/Login"), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
            }));
            await web.GetAsync("/Account/SelectTenant");
            Assert.Contains("calendar.example.com", await web.GetStringAsync("/Availability"));
            var week = await web.GetStringAsync($"/Calendar?date={w.Day:yyyy-MM-dd}");
            Assert.Contains("Busy (Personal)", week);
            Assert.DoesNotContain("Lunch", week); // never the event itself
            await s.Get<BusyCalendarService>().SetShowOnCalendarAsync(cal, false);
            Assert.Empty(await s.Get<BusyCalendarService>().BlocksAsync(w.Day, w.Day, null));
            await s.Get<BusyCalendarService>().DeleteAsync(cal);
            Assert.False(await s.Get<OesDbContext>().BusyBlocks.AnyAsync(b => b.BusyCalendarId == cal));
        }
    }

    [Fact]
    public async Task A_booking_lands_on_the_calendar_but_stays_out_of_the_crm_until_staff_accept_it()
    {
        var w = await WorldAsync();
        Guid existing;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
            existing = await s.Get<ContactService>().CreateAsync(new ContactInput
            { FirstName = "Patricia", LastName = "Booker", OwnerUserId = w.Admin.Id, Methods = [new MethodInput(ContactMethodKinds.Email, "Pat@Example.com", null, true)] });

        BookedResult booked, other;
        await using (var p = await PublicAsync(w))
        {
            booked = await p.Get<BookingService>().BookAsync("roof-inspection", Request(w.Day, Nine), null);
            other = await p.Get<BookingService>().BookAsync("roof-inspection", Request(w.Day, Eleven, "Newt", "newt@example.com", "(402) 555-0150"), null);
        }
        var mail = Directory.GetFiles(Path.Combine(fx.FilesRoot, "mail"), "*.html").Select(File.ReadAllText).Single(f => f.Contains("pat@example.com") && f.Contains(booked.ManageToken));
        Assert.Matches(ManageLink(), mail);

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(1, await db.Contacts.CountAsync(c => c.LastName == "Booker")); // nothing written yet
            var b = await db.Bookings.SingleAsync(x => x.Id == booked.BookingId);
            var appt = await db.Appointments.SingleAsync(a => a.Id == b.AppointmentId);
            Assert.Equal(("77 Booking Blvd, Lincoln, NE 68502", AppointmentStatuses.Scheduled), (appt.Location, appt.Status));
            Assert.Null(appt.EntityType);

            var service = s.Get<BookingService>();
            var row = Assert.Single(await service.InboxAsync(onlyNew: true), r => r.Booking.Id == booked.BookingId);
            var match = Assert.Single(row.Matches);
            Assert.Equal((existing, "email match"), (match.ContactId, match.Why));

            var oppId = await service.AcceptAsync(booked.BookingId, existing);
            Assert.Equal(1, await db.Contacts.CountAsync(c => c.LastName == "Booker")); // linked, not duplicated
            var opp = await db.Opportunities.SingleAsync(o => o.Id == oppId);
            Assert.Equal((existing, "online_booking", b.HostUserId), (opp.PrimaryContactId, opp.SourceKey, opp.OwnerUserId!.Value));
            Assert.Equal((EntityTypes.Opportunity, oppId), ((await db.Appointments.SingleAsync(a => a.Id == b.AppointmentId)).EntityType, (await db.Appointments.SingleAsync(a => a.Id == b.AppointmentId)).EntityId!.Value));
            // The existing contact had no phone, so the visitor's text tick box isn't applied to it.
            Assert.All((await s.Get<ConsentService>().CurrentAsync(existing)).Where(c => c.Channel == Channels.Sms), c => Assert.Equal(ConsentStatuses.Unknown, c.Status));
            await Assert.ThrowsAsync<CrmRuleException>(() => service.AcceptAsync(booked.BookingId, null)); // only once

            await service.AcceptAsync(other.BookingId, null);
            var newt = await db.Contacts.Include(c => c.Methods).SingleAsync(c => c.FirstName == "Newt");
            Assert.Equal(("online_booking", 2), (newt.SourceKey, newt.Methods.Count));
            Assert.Contains(await s.Get<ConsentService>().CurrentAsync(newt.Id), c => c.Channel == Channels.Sms && c.Status == ConsentStatuses.OptedIn);
            // The appointment shows on the new contact's property via the opportunity.
            var onProperty = await s.Get<WorkService>().AppointmentsAsync(w.Day, w.Day, null, EntityTypes.Contact, newt.Id);
            Assert.Single(onProperty);
        }
    }

    [Fact]
    public async Task Customers_can_cancel_from_their_link_and_staff_can_dismiss_spam()
    {
        var w = await WorldAsync();
        BookedResult booked, spam;
        await using (var p = await PublicAsync(w))
        {
            booked = await p.Get<BookingService>().BookAsync("roof-inspection", Request(w.Day, Nine), null);
            spam = await p.Get<BookingService>().BookAsync("roof-inspection", Request(w.Day, Eleven, "Spam", "spam@example.com", "(402) 555-0199"), null);
            await Assert.ThrowsAsync<CrmRuleException>(() => p.Get<BookingService>().BookAsync("roof-inspection", Request(w.Day, Ten).Also(r => r.Website = "http://bot"), null));
        }

        var client = fx.App.Browser();
        var page = await client.GetAsync($"/portal/b/{booked.ManageToken}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Roof inspection", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portal/b/not-a-token")).StatusCode);

        await using (var s = fx.ScopeFor(null)) Assert.True(await s.Get<BookingService>().CancelByCustomerAsync(booked.ManageToken));
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            var b = await db.Bookings.SingleAsync(x => x.Id == booked.BookingId);
            Assert.Equal((BookingStatuses.Cancelled, (string?)null), (b.Status, b.SlotKey));
            Assert.Equal(AppointmentStatuses.Cancelled, (await db.Appointments.SingleAsync(a => a.Id == b.AppointmentId)).Status);

            await s.Get<BookingService>().DismissAsync(spam.BookingId);
            Assert.Equal(BookingReview.Dismissed, (await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == spam.BookingId)).ReviewStatus);
            Assert.False(await db.Contacts.AnyAsync(c => c.FirstName == "Spam"));
            Assert.Empty(await s.Get<BookingService>().InboxAsync(onlyNew: true));
        }
        await using (var p = await PublicAsync(w)) // both times are open again
            Assert.Contains(Nine, (await p.Get<BookingService>().SlotsAsync("roof-inspection"))[w.Day]);
    }

    [Fact]
    public async Task Public_pages_and_hours_permissions()
    {
        var w = await WorldAsync();
        var client = fx.App.Browser();
        var company = await client.GetAsync($"/book/{w.Slug}");
        Assert.Equal(HttpStatusCode.Redirect, company.StatusCode); // one type: straight to it
        Assert.Equal($"/book/{w.Slug}/roof-inspection", company.Headers.Location!.OriginalString);
        var type = await client.GetAsync($"/book/{w.Slug}/roof-inspection");
        Assert.Equal(HttpStatusCode.OK, type.StatusCode);
        Assert.Contains("9:00 AM", await type.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/book/no-such-company")).StatusCode);

        await using var s = fx.ScopeAs(w.Tenant, w.Rep);
        var b = s.Get<BookingService>();
        await Assert.ThrowsAsync<RecordForbiddenException>(() => b.SaveHoursAsync(w.Admin.Id, []));
        await Assert.ThrowsAsync<CrmRuleException>(() => b.SaveHoursAsync(w.Rep.Id, [(DayOfWeek.Monday, Nine, Eleven), (DayOfWeek.Monday, Ten, new TimeOnly(13, 0))]));
        await Assert.ThrowsAsync<RecordForbiddenException>(() => b.InboxAsync(true));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
