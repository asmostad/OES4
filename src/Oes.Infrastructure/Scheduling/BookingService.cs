using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Booking;
using Oes.Domain.Common;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Work;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;
using Oes.Infrastructure.Tenancy;
using Oes.Infrastructure.Work;
using BookingEntity = Oes.Domain.Booking.Booking;

namespace Oes.Infrastructure.Scheduling;

public sealed class BookingTypeInput
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public int SlotIntervalMinutes { get; set; } = 30;
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; } = 30;
    public int MinNoticeMinutes { get; set; } = 720;
    public int DaysAhead { get; set; } = 21;
    public int? DailyCapPerHost { get; set; }
    public string AppointmentTypeKey { get; set; } = "inspection";
    public bool AsksAddress { get; set; } = true;
    public bool CreatesOpportunity { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public List<Guid> HostUserIds { get; set; } = [];
}

public sealed class BookingRequest
{
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    public bool TextConsent { get; set; }
    /// <summary>Hidden field people never see; bots fill it in.</summary>
    public string? Website { get; set; }
}

public sealed record PublicCompany(Guid TenantId, string Name, string Slug);
public sealed record PublicBookingType(string Slug, string Name, string? Description, int DurationMinutes, bool AsksAddress);
public sealed record BookedResult(Guid BookingId, DateTime StartsLocal, string HostName, string ManageToken);
public sealed record ManageView(string Company, string TypeName, DateTime StartsLocal, int DurationMinutes, string? Address, string HostName, bool Cancelled, bool Past);
public sealed record ContactMatch(Guid ContactId, string Name, string Why);
public sealed record InboxRow(BookingEntity Booking, string TypeName, string HostName, DateTime StartsLocal, List<ContactMatch> Matches);

/// <summary>
/// Online booking. A public booking goes straight onto the host's calendar and into a staff
/// inbox; contacts, properties and opportunities are only created (or linked) when staff accept
/// it, so anonymous input never writes onto existing CRM records and spam never reaches them.
/// </summary>
public sealed class BookingService(
    OesDbContext db, RecordScope scope, ICurrentUser user, IClock clock, TenantContext tenant, TenantTime time,
    NotificationService notifications, AccessTokenService tokens, ISystemMailer mailer, IConfiguration config,
    ContactService contacts, PropertyService properties, OpportunityService opportunities, ConsentService consent, RecordItems items)
{
    public static readonly TimeSpan ManageLinkLifetime = TimeSpan.FromDays(120);

    // ---------------- Admin: booking types and hours ----------------

    public async Task<List<BookingType>> TypesAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        return await db.BookingTypes.Include(t => t.Hosts).OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<BookingType> GetTypeAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        return await db.BookingTypes.Include(t => t.Hosts).FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new RecordNotFoundException("Booking type");
    }

    public async Task<Guid> SaveTypeAsync(Guid? id, BookingTypeInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give it a name.");
        var slug = Slugify(string.IsNullOrWhiteSpace(input.Slug) ? name : input.Slug);
        if (slug.Length is < 2 or > 60) throw new CrmRuleException("The link name needs 2 to 60 letters or numbers.");
        if (input.DurationMinutes is < 10 or > 480) throw new CrmRuleException("Length must be 10 minutes to 8 hours.");
        if (input.SlotIntervalMinutes is < 5 or > 240) throw new CrmRuleException("Offer start times every 5 to 240 minutes.");
        if (input.BufferBeforeMinutes is < 0 or > 240 || input.BufferAfterMinutes is < 0 or > 240) throw new CrmRuleException("Buffers can be up to 4 hours.");
        if (input.MinNoticeMinutes is < 0 or > 30 * 24 * 60) throw new CrmRuleException("Notice can be up to 30 days.");
        if (input.DaysAhead is < 1 or > 120) throw new CrmRuleException("Show 1 to 120 days ahead.");
        if (input.DailyCapPerHost is < 1 or > 50) throw new CrmRuleException("The daily limit must be 1 to 50, or blank.");
        var hosts = input.HostUserIds.Distinct().ToList();
        if (input.IsActive && hosts.Count == 0) throw new CrmRuleException("Pick at least one person who takes these appointments.");
        var members = await db.Memberships.Where(m => hosts.Contains(m.UserId) && m.Status == Domain.Tenancy.MembershipStatus.Active).Select(m => m.UserId).ToListAsync(ct);
        if (members.Count != hosts.Count) throw new CrmRuleException("Hosts must be active users.");
        if (await db.BookingTypes.AnyAsync(t => t.Slug == slug && t.Id != id, ct)) throw new CrmRuleException("Another booking type uses that link name.");

        var t = id is { } existing ? await GetTypeAsync(existing, ct) : db.BookingTypes.Add(new BookingType()).Entity;
        t.Name = name; t.Slug = slug; t.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim()[..Math.Min(input.Description.Trim().Length, 1000)];
        t.DurationMinutes = input.DurationMinutes; t.SlotIntervalMinutes = input.SlotIntervalMinutes;
        t.BufferBeforeMinutes = input.BufferBeforeMinutes; t.BufferAfterMinutes = input.BufferAfterMinutes;
        t.MinNoticeMinutes = input.MinNoticeMinutes; t.DaysAhead = input.DaysAhead; t.DailyCapPerHost = input.DailyCapPerHost;
        t.AppointmentTypeKey = string.IsNullOrWhiteSpace(input.AppointmentTypeKey) ? "inspection" : input.AppointmentTypeKey;
        t.AsksAddress = input.AsksAddress; t.CreatesOpportunity = input.CreatesOpportunity; t.IsActive = input.IsActive;
        t.Hosts.RemoveAll(h => !hosts.Contains(h.UserId));
        foreach (var h in hosts.Where(h => t.Hosts.All(x => x.UserId != h))) t.Hosts.Add(new BookingTypeHost { UserId = h });
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public static string Slugify(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public async Task<List<AvailabilityRule>> HoursAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureHoursAccessAsync(userId, ct);
        return await db.AvailabilityRules.Where(r => r.UserId == userId).OrderBy(r => r.DayOfWeek).ThenBy(r => r.StartTime).ToListAsync(ct);
    }

    /// <summary>Replaces a user's weekly hours. People can set their own; booking.manage can set anyone's.</summary>
    public async Task SaveHoursAsync(Guid userId, IReadOnlyList<(DayOfWeek Day, TimeOnly Start, TimeOnly End)> hours, CancellationToken ct = default)
    {
        await EnsureHoursAccessAsync(userId, ct);
        foreach (var g in hours.GroupBy(h => h.Day))
        {
            var list = g.OrderBy(h => h.Start).ToList();
            if (list.Any(h => h.End <= h.Start)) throw new CrmRuleException($"{g.Key}: the end time must be after the start.");
            for (var i = 1; i < list.Count; i++)
                if (list[i].Start < list[i - 1].End) throw new CrmRuleException($"{g.Key}: those hours overlap.");
        }
        db.AvailabilityRules.RemoveRange(await db.AvailabilityRules.Where(r => r.UserId == userId).ToListAsync(ct));
        foreach (var h in hours) db.AvailabilityRules.Add(new AvailabilityRule { UserId = userId, DayOfWeek = h.Day, StartTime = h.Start, EndTime = h.End });
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureHoursAccessAsync(Guid userId, CancellationToken ct)
    {
        if (userId == user.UserId) return;
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        if (!await db.Memberships.AnyAsync(m => m.UserId == userId, ct)) throw new RecordNotFoundException("User");
    }

    // ---------------- Public: find the company, list types, offer slots ----------------

    /// <summary>Finds an active company by its public slug and switches this scope to it. Null when unknown.</summary>
    public async Task<PublicCompany?> OpenCompanyAsync(string? slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 60) return null;
        var t = await db.Tenants.Where(x => x.Slug == slug.ToLower() && x.Status == Domain.Tenancy.TenantStatus.Active)
            .Select(x => new { x.Id, x.Name, x.Slug }).FirstOrDefaultAsync(ct);
        if (t is null) return null;
        if (tenant.TenantId is { } current && current != t.Id) return null; // a signed-in user's own company only
        tenant.SetTenant(t.Id);
        return new PublicCompany(t.Id, t.Name, t.Slug);
    }

    public Task<List<PublicBookingType>> PublicTypesAsync(CancellationToken ct = default) =>
        db.BookingTypes.Where(t => t.IsActive && t.Hosts.Any()).OrderBy(t => t.Name)
            .Select(t => new PublicBookingType(t.Slug, t.Name, t.Description, t.DurationMinutes, t.AsksAddress)).ToListAsync(ct);

    private Task<BookingType?> PublicTypeAsync(string slug, CancellationToken ct) =>
        db.BookingTypes.Include(t => t.Hosts).FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive, ct);

    public async Task<PublicBookingType?> PublicTypeInfoAsync(string slug, CancellationToken ct = default) =>
        await PublicTypeAsync(slug, ct) is { } t ? new PublicBookingType(t.Slug, t.Name, t.Description, t.DurationMinutes, t.AsksAddress) : null;

    /// <summary>Open start times (company-local) per day, from today through the type's booking window.</summary>
    public async Task<SortedDictionary<DateOnly, List<TimeOnly>>> SlotsAsync(string typeSlug, CancellationToken ct = default)
    {
        var result = new SortedDictionary<DateOnly, List<TimeOnly>>();
        if (await PublicTypeAsync(typeSlug, ct) is not { } t || t.Hosts.Count == 0) return result;
        var today = await time.TodayAsync();
        var free = await FreeHostsAsync(t, today, today.AddDays(t.DaysAhead - 1), ct);
        foreach (var ((day, start), hosts) in free.Where(f => f.Value.Count > 0))
        {
            if (!result.TryGetValue(day, out var list)) result[day] = list = [];
            list.Add(start);
        }
        foreach (var l in result.Values) l.Sort();
        return result;
    }

    /// <summary>For each candidate slot in [from, to], the hosts free to take it.</summary>
    private async Task<Dictionary<(DateOnly Day, TimeOnly Start), List<Guid>>> FreeHostsAsync(BookingType t, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var hostIds = t.Hosts.Select(h => h.UserId).ToList();
        var activeHosts = await db.Memberships.Where(m => hostIds.Contains(m.UserId) && m.Status == Domain.Tenancy.MembershipStatus.Active).Select(m => m.UserId).ToListAsync(ct);
        var rules = await db.AvailabilityRules.Where(r => activeHosts.Contains(r.UserId)).ToListAsync(ct);
        var (rangeStart, rangeEnd) = await time.DayRangeUtcAsync(from, to);
        var pad = TimeSpan.FromMinutes(Math.Max(t.BufferBeforeMinutes, t.BufferAfterMinutes) + t.DurationMinutes);
        var busy = await db.Appointments.Where(a => activeHosts.Contains(a.AssignedToUserId)
                                                    && a.Status != AppointmentStatuses.Cancelled
                                                    && a.EndsAt > rangeStart - pad && a.StartsAt < rangeEnd + pad)
            .Select(a => new { Host = a.AssignedToUserId, a.StartsAt, a.EndsAt }).ToListAsync(ct);
        var booked = await db.Bookings.Where(b => b.BookingTypeId == t.Id && b.Status == BookingStatuses.Booked && b.StartsAt >= rangeStart && b.StartsAt < rangeEnd)
            .Select(b => new { b.HostUserId, b.StartsAt }).ToListAsync(ct);
        var earliest = clock.UtcNow.AddMinutes(t.MinNoticeMinutes);
        var zone = await time.ZoneAsync();

        var result = new Dictionary<(DateOnly, TimeOnly), List<Guid>>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {

            foreach (var host in activeHosts)
            {
                if (t.DailyCapPerHost is { } cap)
                {
                    var onDay = booked.Count(b => b.HostUserId == host && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(b.StartsAt, zone)) == day);
                    if (onDay >= cap) continue;
                }
                foreach (var r in rules.Where(r => r.UserId == host && r.DayOfWeek == day.DayOfWeek))
                {
                    var windowEnd = r.EndTime.Hour * 60 + r.EndTime.Minute;
                    for (var m = r.StartTime.Hour * 60 + r.StartTime.Minute; m + t.DurationMinutes <= windowEnd; m += t.SlotIntervalMinutes)
                    {
                        var s = new TimeOnly(m / 60, m % 60);
                        var startUtc = await time.ToUtcAsync(day, s);
                        if (startUtc < earliest) continue;
                        var blockFrom = startUtc.AddMinutes(-t.BufferBeforeMinutes);
                        var blockTo = startUtc.AddMinutes(t.DurationMinutes + t.BufferAfterMinutes);
                        if (busy.Any(a => a.Host == host && a.StartsAt < blockTo && a.EndsAt > blockFrom)) continue;
                        if (!result.TryGetValue((day, s), out var list)) result[(day, s)] = list = [];
                        list.Add(host);
                    }
                }
            }
        }
        return result;
    }

    // ---------------- Public: book ----------------

    public async Task<BookedResult> BookAsync(string typeSlug, BookingRequest req, string? sourceIp, CancellationToken ct = default)
    {
        var t = await PublicTypeAsync(typeSlug, ct) ?? throw new RecordNotFoundException("Booking type");
        if (!string.IsNullOrEmpty(req.Website)) throw new CrmRuleException("Something went wrong. Please call us to book.");
        string Clean(string? s, int max) => (s ?? "").Trim() is var v && v.Length > max ? v[..max] : (s ?? "").Trim();
        var first = Clean(req.FirstName, 100);
        var last = Clean(req.LastName, 100);
        var email = Clean(req.Email, 320).ToLowerInvariant();
        var phone = ContactValue.Phone(Clean(req.Phone, 30));
        if (first.Length == 0 || last.Length == 0) throw new CrmRuleException("Enter your first and last name.");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || !email.Contains('.')) throw new CrmRuleException("Enter a valid email address.");
        if (phone.Length < 11) throw new CrmRuleException("Enter a phone number we can reach you on.");
        if (t.AsksAddress && (string.IsNullOrWhiteSpace(req.Line1) || string.IsNullOrWhiteSpace(req.City) || string.IsNullOrWhiteSpace(req.PostalCode)))
            throw new CrmRuleException("Enter the property address.");

        var free = await FreeHostsAsync(t, req.Date, req.Date, ct);
        var hosts = free.GetValueOrDefault((req.Date, req.Time)) ?? [];
        if (hosts.Count == 0) throw new CrmRuleException("Sorry, that time isn't available any more. Please pick another.");

        // Round robin: whoever has the fewest upcoming bookings takes it.
        var now = clock.UtcNow;
        var load = await db.Bookings.Where(b => hosts.Contains(b.HostUserId) && b.Status == BookingStatuses.Booked && b.StartsAt >= now)
            .GroupBy(b => b.HostUserId).Select(g => new { Host = g.Key, Count = g.Count() }).ToListAsync(ct);
        var ordered = hosts.OrderBy(h => load.FirstOrDefault(l => l.Host == h)?.Count ?? 0).ThenBy(h => h).ToList();

        var startsAt = await time.ToUtcAsync(req.Date, req.Time);
        var endsAt = startsAt.AddMinutes(t.DurationMinutes);
        var address = t.AsksAddress ? $"{Clean(req.Line1, 200)}, {Clean(req.City, 100)}, {Clean(req.State, 30).ToUpperInvariant()} {Clean(req.PostalCode, 20)}".Replace(" ,", ",").Trim() : null;
        var notes = Clean(req.Notes, 2000);

        foreach (var host in ordered)
        {
            var appt = new Appointment
            {
                Title = $"{t.Name}: {first} {last}", TypeKey = t.AppointmentTypeKey, StartsAt = startsAt, EndsAt = endsAt, Location = address,
                AssignedToUserId = host, Status = AppointmentStatuses.Scheduled,
                Notes = $"Booked online. {ContactValue.FormatPhone(phone, phone)} · {email}{(notes.Length > 0 ? "\n" + notes : "")}",
            };
            var booking = new BookingEntity
            {
                BookingTypeId = t.Id, HostUserId = host, AppointmentId = appt.Id, StartsAt = startsAt, EndsAt = endsAt,
                FirstName = first, LastName = last, Email = email, Phone = phone, Notes = notes.Length > 0 ? notes : null,
                Line1 = t.AsksAddress ? Clean(req.Line1, 200) : null, City = t.AsksAddress ? Clean(req.City, 100) : null,
                State = t.AsksAddress ? Clean(req.State, 30).ToUpperInvariant() : null, PostalCode = t.AsksAddress ? Clean(req.PostalCode, 20) : null,
                TextConsent = req.TextConsent, SourceIp = sourceIp?[..Math.Min(sourceIp.Length, 64)], SlotKey = SlotKey(host, startsAt),
            };
            db.Appointments.Add(appt);
            db.Bookings.Add(booking);
            notifications.Notify(host, "booking.new", $"New online booking: {first} {last}",
                $"{t.Name}, {(await time.ToLocalAsync(startsAt)):ddd MMM d, h:mm tt}", "/Bookings");
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Someone took this host's slot a moment ago: try the next free host.
                db.ChangeTracker.Clear();
                continue;
            }

            var token = await tokens.IssueAsync(TokenPurposes.BookingManage, "booking", booking.Id, ManageLinkLifetime, ct);
            var hostName = await db.Users.Where(u => u.Id == host).Select(u => u.FirstName + " " + u.LastName).FirstAsync(ct);
            var startsLocal = await time.ToLocalAsync(startsAt);
            await SendConfirmationAsync(booking, t, startsLocal, hostName, token, ct);
            return new BookedResult(booking.Id, startsLocal, hostName, token);
        }
        throw new CrmRuleException("Sorry, that time was just taken. Please pick another.");
    }

    private static string SlotKey(Guid host, DateTime startsAt) => $"{host:N}|{startsAt.Ticks}";

    private async Task SendConfirmationAsync(BookingEntity b, BookingType t, DateTime startsLocal, string hostName, string token, CancellationToken ct)
    {
        var company = await db.Tenants.Where(x => x.Id == b.TenantId).Select(x => x.Name).FirstAsync(ct);
        var link = $"{(config["App:BaseUrl"] ?? "").TrimEnd('/')}/portal/b/{token}";
        var enc = (string s) => System.Net.WebUtility.HtmlEncode(s);
        var when = startsLocal.ToString("dddd, MMMM d 'at' h:mm tt");
        var html = $"<p>Hi {enc(b.FirstName)},</p><p>You're booked with {enc(company)}: <strong>{enc(t.Name)}</strong> on {enc(when)}" +
                   $"{(b.Line1 is null ? "" : $" at {enc(b.Line1)}, {enc(b.City ?? "")}")}. {enc(hostName)} will see you then.</p>" +
                   $"<p>Need to cancel? <a href=\"{enc(link)}\">Manage your appointment</a>.</p>";
        try { await mailer.SendAsync(new SystemEmail(b.Email, $"Your appointment with {company}", html), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* the booking stands; the page shows the details */ }
    }

    // ---------------- Public: manage (cancel) ----------------

    private async Task<(BookingEntity B, BookingType T)?> FromTokenAsync(string? raw, CancellationToken ct)
    {
        var tok = await tokens.ResolveAsync(raw, TokenPurposes.BookingManage, ct);
        if (tok is null || tok.EntityType != "booking") return null;
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == tok.EntityId, ct);
        if (b is null) return null;
        var t = await db.BookingTypes.FirstAsync(x => x.Id == b.BookingTypeId, ct);
        return (b, t);
    }

    public async Task<ManageView?> ManageViewAsync(string? raw, CancellationToken ct = default)
    {
        if (await FromTokenAsync(raw, ct) is not { } x) return null;
        var company = await db.Tenants.Where(t => t.Id == x.B.TenantId).Select(t => t.Name).FirstAsync(ct);
        var host = await db.Users.Where(u => u.Id == x.B.HostUserId).Select(u => u.FirstName + " " + u.LastName).FirstAsync(ct);
        return new ManageView(company, x.T.Name, await time.ToLocalAsync(x.B.StartsAt), x.T.DurationMinutes,
            x.B.Line1 is null ? null : $"{x.B.Line1}, {x.B.City}", host, x.B.Status == BookingStatuses.Cancelled, x.B.StartsAt <= clock.UtcNow);
    }

    public async Task<bool> CancelByCustomerAsync(string? raw, CancellationToken ct = default)
    {
        if (await FromTokenAsync(raw, ct) is not { } x) return false;
        if (x.B.Status == BookingStatuses.Cancelled) return true;
        if (x.B.StartsAt <= clock.UtcNow) throw new CrmRuleException("This appointment has already started. Please call us.");
        await CancelAsync(x.B, ct);
        notifications.Notify(x.B.HostUserId, "booking.cancelled", $"Booking cancelled: {x.B.FirstName} {x.B.LastName}",
            $"{x.T.Name}, {(await time.ToLocalAsync(x.B.StartsAt)):ddd MMM d, h:mm tt}", "/Calendar");
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task CancelAsync(BookingEntity b, CancellationToken ct)
    {
        b.Status = BookingStatuses.Cancelled;
        b.CancelledAt = clock.UtcNow;
        b.SlotKey = null;
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == b.AppointmentId, ct);
        if (appt is not null) appt.Status = AppointmentStatuses.Cancelled;
    }

    // ---------------- Staff: review inbox ----------------

    public async Task<List<InboxRow>> InboxAsync(bool onlyNew, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        var q = db.Bookings.AsQueryable();
        if (onlyNew) q = q.Where(b => b.ReviewStatus == BookingReview.New && b.Status == BookingStatuses.Booked);
        var list = await q.OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct);
        var typeNames = await db.BookingTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var hostIds = list.Select(b => b.HostUserId).Distinct().ToList();
        var hosts = await db.Users.Where(u => hostIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        var rows = new List<InboxRow>();
        foreach (var b in list)
            rows.Add(new InboxRow(b, typeNames.GetValueOrDefault(b.BookingTypeId, "?"), hosts.GetValueOrDefault(b.HostUserId, "?"),
                await time.ToLocalAsync(b.StartsAt), b.ReviewStatus == BookingReview.New ? await MatchesAsync(b, ct) : []));
        return rows;
    }

    /// <summary>Existing contacts with the same email or phone, so staff can link instead of duplicating.</summary>
    private async Task<List<ContactMatch>> MatchesAsync(BookingEntity b, CancellationToken ct)
    {
        var hits = await (from m in db.ContactMethods
                          where m.NormalizedValue == b.Email || m.NormalizedValue == b.Phone
                          join c in db.Contacts on m.ContactId equals c.Id
                          where c.ArchivedAt == null
                          select new { c.Id, c.DisplayName, m.Kind }).ToListAsync(ct);
        return hits.GroupBy(h => h.Id).Select(g => new ContactMatch(g.Key, g.First().DisplayName,
            string.Join(" and ", g.Select(x => x.Kind == ContactMethodKinds.Email ? "email" : "phone").Distinct()) + " match")).ToList();
    }

    /// <summary>Creates the contact (or uses <paramref name="existingContactId"/>), property and opportunity, and files the appointment on them.</summary>
    public async Task<Guid> AcceptAsync(Guid bookingId, Guid? existingContactId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct) ?? throw new RecordNotFoundException("Booking");
        if (b.ReviewStatus != BookingReview.New) throw new CrmRuleException("This booking was already reviewed.");
        var t = await db.BookingTypes.FirstAsync(x => x.Id == b.BookingTypeId, ct);

        Guid contactId;
        if (existingContactId is { } cid)
        {
            await scope.EnsureCanSeeAsync(EntityTypes.Contact, cid);
            contactId = cid;
        }
        else
        {
            contactId = await contacts.CreateAsync(new ContactInput
            {
                FirstName = b.FirstName, LastName = b.LastName, OwnerUserId = b.HostUserId, SourceKey = "online_booking",
                Methods = [new MethodInput(ContactMethodKinds.Email, b.Email, null, true), new MethodInput(ContactMethodKinds.Mobile, b.Phone, null, true)],
            }, ct);
        }

        Guid? propertyId = null;
        if (b.Line1 is not null)
        {
            var input = new PropertyInput { Line1 = b.Line1, City = b.City ?? "", State = b.State ?? "", PostalCode = b.PostalCode ?? "" };
            try { propertyId = await properties.CreateAsync(input, ct); }
            catch (DuplicatePropertyException dup) { propertyId = dup.ExistingId; }
            if (!await db.PropertyContacts.AnyAsync(p => p.PropertyId == propertyId && p.ContactId == contactId && p.EndedOn == null, ct))
                await properties.LinkContactAsync(propertyId.Value, contactId, "owner", isPrimary: existingContactId is null, ct);
        }

        Guid? oppId = null;
        if (t.CreatesOpportunity)
            oppId = await opportunities.CreateAsync(new OpportunityInput
            {
                PrimaryContactId = contactId, PropertyId = propertyId, OwnerUserId = b.HostUserId, SourceKey = "online_booking",
                Description = b.Notes,
            }, ct);

        // The visitor's own tick box: texting consent for the number they typed.
        if (b.TextConsent && await db.ContactMethods.AnyAsync(m => m.ContactId == contactId && m.NormalizedValue == b.Phone, ct))
            await consent.RecordAsync(contactId, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "web_form",
                $"Ticked \"text me about this appointment\" when booking online ({b.CreatedAt:yyyy-MM-dd})", ct);

        var appt = await db.Appointments.FirstAsync(a => a.Id == b.AppointmentId, ct);
        (appt.EntityType, appt.EntityId) = oppId is { } o ? (EntityTypes.Opportunity, o) : (EntityTypes.Contact, contactId);
        await items.LinkItemAsync(ItemTypes.Appointment, appt.Id, appt.EntityType, appt.EntityId.Value, ct);
        b.ContactId = contactId; b.OpportunityId = oppId;
        b.ReviewStatus = BookingReview.Accepted; b.ReviewedAt = clock.UtcNow; b.ReviewedByUserId = user.UserId;
        await db.SaveChangesAsync(ct);
        return oppId ?? contactId;
    }

    /// <summary>Spam or a mistake: cancels the appointment and keeps it out of the CRM.</summary>
    public async Task DismissAsync(Guid bookingId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BookingManage);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct) ?? throw new RecordNotFoundException("Booking");
        if (b.ReviewStatus != BookingReview.New) throw new CrmRuleException("This booking was already reviewed.");
        await CancelAsync(b, ct);
        b.ReviewStatus = BookingReview.Dismissed; b.ReviewedAt = clock.UtcNow; b.ReviewedByUserId = user.UserId;
        await db.SaveChangesAsync(ct);
    }
}
