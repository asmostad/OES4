using Microsoft.EntityFrameworkCore;
using Oes.Domain.Booking;
using Oes.Domain.Crm;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Scheduling;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class EditAndRescheduleTests(MySqlFixture fx)
{
    private static readonly TimeOnly Nine = new(9, 0), Ten = new(10, 0), Eleven = new(11, 0);

    [Fact]
    public async Task Editing_an_appointment_moves_it_tells_the_assignee_and_respects_ownership()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var other = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid appt, task;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            appt = await s.Get<WorkService>().CreateAppointmentAsync(new AppointmentInput { Title = "Inspection", Date = new DateOnly(2026, 12, 3), StartTime = Nine, AssignedToUserId = rep.Id });
            task = await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Order shingles", AssignedToUserId = rep.Id });
        }
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            await s.Get<WorkService>().UpdateAppointmentAsync(appt, new AppointmentInput
            { Title = "Inspection (moved)", Date = new DateOnly(2026, 12, 4), StartTime = new TimeOnly(13, 30), DurationMinutes = 90, AssignedToUserId = rep.Id, Location = "12 Elm" });
            var a = await s.Get<OesDbContext>().Appointments.SingleAsync(x => x.Id == appt);
            Assert.Equal((new DateTime(2026, 12, 4, 19, 30, 0), new DateTime(2026, 12, 4, 21, 0, 0), "12 Elm"), (a.StartsAt, a.EndsAt, a.Location));
            await s.Get<WorkService>().UpdateTaskAsync(task, new TaskInput { Title = "Order shingles (GAF)", AssignedToUserId = other.Id, DueDate = new DateOnly(2026, 12, 5) });
        }
        await using (var s = fx.ScopeAs(t.Id, rep))
        {
            Assert.Contains(await s.Get<NotificationService>().LatestAsync(), n => n.Kind == "appointment.moved" && n.Title == "Moved: Inspection (moved)");
        }
        await using (var s = fx.ScopeAs(t.Id, other))
        {
            Assert.Contains(await s.Get<NotificationService>().LatestAsync(), n => n.Title == "Task reassigned to you: Order shingles (GAF)");
            // Not theirs (and no calendar.manage): can't edit someone else's appointment.
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<WorkService>().UpdateAppointmentAsync(appt,
                new AppointmentInput { Title = "Mine now", Date = new DateOnly(2026, 12, 4), StartTime = Nine }));
        }
        await using (var s = fx.ScopeAs(t.Id, rep)) // the assignee can edit, but not hand it to someone else
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<WorkService>().UpdateAppointmentAsync(appt,
                new AppointmentInput { Title = "Inspection", Date = new DateOnly(2026, 12, 4), StartTime = Nine, AssignedToUserId = other.Id }));
    }

    private sealed record BookingWorld(Guid Tenant, string Slug, AppUser Admin, DateOnly Day);

    private async Task<BookingWorld> BookingWorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var b = s.Get<BookingService>();
        await b.SaveTypeAsync(null, new BookingTypeInput { Name = "Roof inspection", DurationMinutes = 60, SlotIntervalMinutes = 60, BufferAfterMinutes = 0, MinNoticeMinutes = 0, DaysAhead = 7, HostUserIds = [admin.Id] });
        await b.SaveHoursAsync(admin.Id, Enum.GetValues<DayOfWeek>().Select(d => (d, Nine, new TimeOnly(12, 0))).ToList());
        return new BookingWorld(t.Id, t.Slug, admin, (await s.Get<TenantTime>().TodayAsync()).AddDays(1));
    }

    private async Task<BookedResult> BookAsync(BookingWorld w, TimeOnly at, string email)
    {
        await using var s = fx.ScopeFor(null);
        await s.Get<BookingService>().OpenCompanyAsync(w.Slug);
        return await s.Get<BookingService>().BookAsync("roof-inspection", new BookingRequest
        { Date = w.Day, Time = at, FirstName = "Ray", LastName = "Schedule", Email = email, Phone = "(402) 555-0188", Line1 = "8 Move St", City = "Lincoln", PostalCode = "68502" }, null);
    }

    [Fact]
    public async Task Customers_can_move_their_booking_to_an_open_time_a_few_times()
    {
        var w = await BookingWorldAsync();
        var mine = await BookAsync(w, Nine, $"ray-{Guid.NewGuid():N}@example.com");
        await BookAsync(w, Ten, $"other-{Guid.NewGuid():N}@example.com"); // 10:00 is taken

        await using (var s = fx.ScopeFor(null))
        {
            var b = s.Get<BookingService>();
            var slots = (await b.RescheduleSlotsAsync(mine.ManageToken))[w.Day];
            Assert.Equal([Eleven], slots); // not its own 9:00, not the taken 10:00
            await Assert.ThrowsAsync<CrmRuleException>(() => b.RescheduleByCustomerAsync(mine.ManageToken, w.Day, Ten));
        }
        await using (var s = fx.ScopeFor(null))
        {
            var moved = await s.Get<BookingService>().RescheduleByCustomerAsync(mine.ManageToken, w.Day, Eleven);
            Assert.Equal(11, moved.Hour);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            var booking = await db.Bookings.SingleAsync(x => x.Id == mine.BookingId);
            var appt = await db.Appointments.SingleAsync(a => a.Id == booking.AppointmentId);
            Assert.Equal((1, appt.StartsAt, (DateTime?)null), (booking.RescheduleCount, booking.StartsAt, booking.ReminderSentAt));
            Assert.Contains(await s.Get<NotificationService>().LatestAsync(), n => n.Kind == "booking.moved");
        }
        // 9:00 is free again for others.
        await using (var s = fx.ScopeFor(null))
        {
            await s.Get<BookingService>().OpenCompanyAsync(w.Slug);
            Assert.Contains(Nine, (await s.Get<BookingService>().SlotsAsync("roof-inspection"))[w.Day]);
        }
        var mails = Directory.GetFiles(Path.Combine(fx.FilesRoot, "mail"), "*.html").Select(File.ReadAllText).Where(m => m.Contains(mine.ManageToken)).ToList();
        Assert.Equal(2, mails.Count); // the original confirmation and the new one

        // Moving it back and forth hits the limit.
        for (var i = 0; i < BookingService.MaxReschedules - 1; i++)
            await using (var s = fx.ScopeFor(null))
                await s.Get<BookingService>().RescheduleByCustomerAsync(mine.ManageToken, w.Day, i % 2 == 0 ? Nine : Eleven);
        await using (var s = fx.ScopeFor(null))
        {
            Assert.False((await s.Get<BookingService>().ManageViewAsync(mine.ManageToken))!.CanReschedule);
            await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<BookingService>().RescheduleByCustomerAsync(mine.ManageToken, w.Day, Nine));
        }
    }

    [Fact]
    public async Task Staff_moving_a_booked_appointment_moves_the_booking_too()
    {
        var w = await BookingWorldAsync();
        var mine = await BookAsync(w, Nine, $"staff-{Guid.NewGuid():N}@example.com");
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            var apptId = await db.Bookings.Where(b => b.Id == mine.BookingId).Select(b => b.AppointmentId).SingleAsync();
            await s.Get<WorkService>().UpdateAppointmentAsync(apptId, new AppointmentInput { Title = "Roof inspection: Ray Schedule", Date = w.Day, StartTime = Eleven, DurationMinutes = 60, AssignedToUserId = w.Admin.Id });
            var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == mine.BookingId);
            var appt = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == apptId);
            Assert.Equal(appt.StartsAt, booking.StartsAt);
            Assert.EndsWith($"|{appt.StartsAt.Ticks}", booking.SlotKey);
        }
    }
}
