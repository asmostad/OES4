using System.Globalization;
using System.Text.RegularExpressions;

namespace Oes.Infrastructure.Scheduling;

public sealed record BusyTime(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Reads the busy times out of an iCalendar (.ics) feed: only when, never what. Handles folded lines,
/// UTC / TZID / floating times and all-day dates, DTEND or DURATION, events marked free (TRANSP) or
/// cancelled, and RRULE for daily, weekly (BYDAY), monthly (by day of month) and yearly repeats with
/// INTERVAL, COUNT, UNTIL and EXDATE. Repeats are expanded on the wall clock in the event's own zone,
/// so a 9 am meeting stays at 9 am across daylight saving. Anything it can't read is skipped.
/// </summary>
public static partial class IcsParser
{
    public const int MaxOccurrences = 5000;

    private sealed record Prop(string Name, Dictionary<string, string> Params, string Value);

    public static List<BusyTime> BusyTimes(string ics, DateTime fromUtc, DateTime toUtc, TimeZoneInfo defaultZone)
    {
        var result = new List<BusyTime>();
        List<Prop>? ev = null;
        foreach (var p in Props(ics))
        {
            if (p.Name == "BEGIN" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = []; continue; }
            if (p.Name == "END" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (ev is not null)
                    try { result.AddRange(Expand(ev, fromUtc, toUtc, defaultZone)); }
                    catch (FormatException) { } // a malformed event is skipped, not the whole feed
                    catch (ArgumentException) { }
                ev = null;
                if (result.Count >= MaxOccurrences) break;
                continue;
            }
            ev?.Add(p);
        }
        return result.Distinct().OrderBy(b => b.StartUtc).Take(MaxOccurrences).ToList();
    }

    private static IEnumerable<Prop> Props(string ics)
    {
        // Unfold: a line starting with a space or tab continues the previous one.
        var lines = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[^1] += raw[1..];
            else lines.Add(raw);
        }
        foreach (var line in lines)
        {
            var colon = ColonOutsideQuotes(line);
            if (colon <= 0) continue;
            var head = line[..colon].Split(';');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in head.Skip(1))
            {
                var eq = part.IndexOf('=');
                if (eq > 0) parameters[part[..eq]] = part[(eq + 1)..].Trim('"');
            }
            yield return new Prop(head[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..].Trim());
        }
    }

    private static int ColonOutsideQuotes(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static IEnumerable<BusyTime> Expand(List<Prop> ev, DateTime fromUtc, DateTime toUtc, TimeZoneInfo defaultZone)
    {
        string? Get(string name) => ev.FirstOrDefault(p => p.Name == name)?.Value;
        if (Get("TRANSP")?.Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase) == true) yield break; // marked "free"
        if (Get("STATUS")?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true) yield break;
        var startProp = ev.FirstOrDefault(p => p.Name == "DTSTART");
        if (startProp is null) yield break;

        var (start, allDay, zone) = Read(startProp, defaultZone);
        TimeSpan length;
        if (ev.FirstOrDefault(p => p.Name == "DTEND") is { } endProp) length = Read(endProp, zone).Local - start;
        else if (Get("DURATION") is { } d) length = Duration(d);
        else length = allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        if (length <= TimeSpan.Zero) yield break;

        var excluded = new HashSet<DateTime>();
        foreach (var ex in ev.Where(p => p.Name == "EXDATE"))
            foreach (var v in ex.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                excluded.Add(Read(new Prop("EXDATE", ex.Params, v), zone).Local);

        IEnumerable<DateTime> starts = Get("RRULE") is { } rule ? Occurrences(start, rule, zone, fromUtc, toUtc) : [start];
        foreach (var local in starts)
        {
            if (excluded.Contains(local)) continue;
            var s = ToUtc(local, zone);
            var e = ToUtc(local + length, zone);
            if (e > fromUtc && s < toUtc) yield return new BusyTime(s, e);
        }
    }

    /// <summary>A DATE or DATE-TIME as wall-clock time in its zone (UTC "Z" times use the UTC zone).</summary>
    private static (DateTime Local, bool AllDay, TimeZoneInfo Zone) Read(Prop p, TimeZoneInfo fallback)
    {
        var v = p.Value;
        if (v.Length == 8 || (p.Params.TryGetValue("VALUE", out var type) && type.Equals("DATE", StringComparison.OrdinalIgnoreCase)))
            return (DateTime.ParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture), true, fallback);
        var utc = v.EndsWith('Z');
        var dt = DateTime.ParseExact(v.TrimEnd('Z')[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return (dt, false, TimeZoneInfo.Utc);
        var zone = p.Params.TryGetValue("TZID", out var tzid) && TimeZoneInfo.TryFindSystemTimeZoneById(tzid, out var z) ? z : fallback;
        return (dt, false, zone);
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (zone == TimeZoneInfo.Utc) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // skipped by daylight saving
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    [GeneratedRegex(@"^(?<sign>[+-])?P(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+)S)?)?$")]
    private static partial Regex DurationPattern();

    private static TimeSpan Duration(string v)
    {
        var m = DurationPattern().Match(v);
        if (!m.Success) throw new FormatException("Bad duration");
        int N(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(N("w") * 7 + N("d"), N("h"), N("m"), N("s"));
        return m.Groups["sign"].Value == "-" ? -span : span;
    }

    private static readonly Dictionary<string, DayOfWeek> Days = new()
    {
        ["SU"] = DayOfWeek.Sunday, ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday,
    };

    /// <summary>Wall-clock starts of a repeating event, up to the window's end (and COUNT/UNTIL).</summary>
    private static IEnumerable<DateTime> Occurrences(DateTime start, string rrule, TimeZoneInfo zone, DateTime fromUtc, DateTime toUtc)
    {
        var parts = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .ToDictionary(x => x[0].ToUpperInvariant(), x => x[1], StringComparer.OrdinalIgnoreCase);
        var freq = parts.GetValueOrDefault("FREQ")?.ToUpperInvariant();
        var interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var i) && i > 0 ? i : 1;
        int? count = int.TryParse(parts.GetValueOrDefault("COUNT"), out var c) && c > 0 ? c : null;
        DateTime? untilUtc = parts.GetValueOrDefault("UNTIL") is { } u
            ? (u.Length == 8 ? ToUtc(DateTime.ParseExact(u, "yyyyMMdd", CultureInfo.InvariantCulture).AddDays(1).AddTicks(-1), zone)
                : u.EndsWith('Z') ? DateTime.SpecifyKind(DateTime.ParseExact(u.TrimEnd('Z')[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture), DateTimeKind.Utc)
                : ToUtc(DateTime.ParseExact(u[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture), zone))
            : null;
        var byDay = (parts.GetValueOrDefault("BYDAY") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Length >= 2 && Days.TryGetValue(d[^2..].ToUpperInvariant(), out var day) ? (DayOfWeek?)day : null)
            .Where(d => d is not null).Select(d => d!.Value).ToHashSet();

        var produced = 0;
        // With no COUNT, old repeats can jump close to the window instead of walking every past occurrence.
        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), zone).AddDays(-8);
        var skipDays = count is null && fromLocal > start ? (int)(fromLocal - start).TotalDays : 0;
        bool Stop(DateTime local) => ToUtc(local, zone) >= toUtc || (untilUtc is { } until && ToUtc(local, zone) > until) || (count is { } n && produced >= n);

        switch (freq)
        {
            case "DAILY":
                for (var d = start.AddDays(skipDays / interval * interval); !Stop(d) && produced < MaxOccurrences; d = d.AddDays(interval)) { produced++; yield return d; }
                break;
            case "WEEKLY":
                if (byDay.Count == 0) byDay.Add(start.DayOfWeek);
                var weekStart = start.Date.AddDays(-(int)start.DayOfWeek).AddDays(skipDays / (7 * interval) * 7 * interval);
                for (var w = weekStart; produced < MaxOccurrences; w = w.AddDays(7 * interval))
                {
                    for (var k = 0; k < 7; k++)
                    {
                        var d = w.AddDays(k) + start.TimeOfDay;
                        if (d < start || !byDay.Contains(d.DayOfWeek)) continue;
                        if (Stop(d)) yield break;
                        produced++;
                        yield return d;
                    }
                    if (ToUtc(w, zone) >= toUtc) yield break;
                }
                break;
            case "MONTHLY":
                for (var m = 0; produced < MaxOccurrences; m += interval)
                {
                    var month = new DateTime(start.Year, start.Month, 1).AddMonths(m);
                    if (ToUtc(month, zone) >= toUtc) yield break;
                    if (start.Day > DateTime.DaysInMonth(month.Year, month.Month)) continue; // e.g. the 31st in a short month
                    var d = new DateTime(month.Year, month.Month, start.Day) + start.TimeOfDay;
                    if (Stop(d)) yield break;
                    produced++;
                    yield return d;
                }
                break;
            case "YEARLY":
                for (var y = 0; produced < MaxOccurrences; y += interval)
                {
                    if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(start.Year + y)) continue;
                    var d = start.AddYears(y);
                    if (Stop(d)) yield break;
                    produced++;
                    yield return d;
                }
                break;
            default:
                yield return start; // an unknown repeat: at least the first one counts
                break;
        }
    }
}
