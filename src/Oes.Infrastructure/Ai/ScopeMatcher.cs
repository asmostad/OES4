using System.Text.RegularExpressions;
using Oes.Domain.Estimating;

namespace Oes.Infrastructure.Ai;

/// <summary>One price list row, as the matcher sees it.</summary>
public sealed record PriceBookEntry(Guid Id, string? Category, string Code, string Activity, string Description, string Unit, decimal UnitPrice);

public static class ScopeMatchKinds
{
    /// <summary>Same item code (and category) and activity.</summary>
    public const string Code = "code";
    /// <summary>Same wording and activity, whatever the code.</summary>
    public const string Description = "description";
    /// <summary>Same item, another activity (e.g. the carrier's R&amp;R against our Replace).</summary>
    public const string Activity = "activity";
    /// <summary>Our description is a cut-off version of the carrier's.</summary>
    public const string Shortened = "shortened";
    /// <summary>Near-identical wording (80%+ of words shared) in the same unit. Flagged to check.</summary>
    public const string Close = "close";
}

/// <summary>The line's match (used as-is), or only a suggestion the person can choose, with why.</summary>
public sealed record ScopeMatch(string? Kind, PriceBookEntry? Match, PriceBookEntry? Suggestion, int? Similarity, string? Note)
{
    public static readonly ScopeMatch None = new(null, null, null, null, null);
}

/// <summary>
/// Matches a carrier scope line to the estimate's price list, in OES3's order: item code + activity; then the same
/// wording (with any "Remove"/"R&amp;R"/"Install"… prefix read as the activity); then the same item at another
/// activity; then our wording cut off (descriptions of 45+ characters that start the carrier's); then the closest
/// wording by shared words. Close wording (80%+) is taken but flagged; 50–80% is only suggested, because another
/// price-list year words things differently. A line whose unit differs from ours never matches by itself (SF vs SQ
/// quantities don't mean the same thing); it gets the item as a suggestion with a note instead.
/// </summary>
public sealed class ScopeMatcher
{
    public const decimal TakeAt = 0.8m;
    public const decimal SuggestAt = 0.5m;
    private const int ShortenedMin = 45;

    private static readonly (string Prefix, string Activity)[] Prefixes =
    [
        ("remove ", Activities.Remove), ("r&r ", Activities.RemoveReplace), ("r & r ", Activities.RemoveReplace),
        ("detach & reset ", Activities.Detach), ("d&r ", Activities.Detach), ("install ", Activities.Install),
        ("material only ", Activities.Replace), ("replace ", Activities.Replace),
    ];

    private static readonly HashSet<string> Stop = ["a", "an", "the", "and", "or", "of", "to", "w", "with", "out", "per", "up", "in", "on", "for", "by"];

    private readonly List<PriceBookEntry> entries;
    private readonly Dictionary<string, List<PriceBookEntry>> byText = [];
    private readonly List<string> longTexts = [];
    private readonly Dictionary<string, List<string>> textsByToken = [];
    private readonly Dictionary<string, string[]> tokensByText = [];

    public ScopeMatcher(IEnumerable<PriceBookEntry> book)
    {
        entries = book.ToList();
        foreach (var e in entries)
        {
            var text = Norm(e.Description);
            if (text.Length == 0) continue;
            if (!byText.TryGetValue(text, out var rows))
            {
                byText[text] = rows = [];
                if (text.Length >= ShortenedMin) longTexts.Add(text);
                var tokens = Tokens(text);
                tokensByText[text] = tokens;
                foreach (var t in tokens)
                {
                    if (!textsByToken.TryGetValue(t, out var list)) textsByToken[t] = list = [];
                    list.Add(text);
                }
            }
            rows.Add(e);
        }
    }

    public ScopeMatch Match(string? category, string? code, string activity, string description, string? unit)
    {
        var u = (unit ?? "").Trim().ToUpperInvariant();

        // 1. Item code (+ category) and activity: the carrier and we both use Xactimate codes.
        if (!string.IsNullOrEmpty(code))
        {
            var sameCode = entries.Where(e => e.Code == code && (category is null || e.Category == category)).ToList();
            if (sameCode.Count > 0)
            {
                var exact = sameCode.FirstOrDefault(e => e.Activity == activity)
                            ?? (activity == Activities.None ? sameCode[0] : null);
                if (exact is not null) return Take(ScopeMatchKinds.Code, exact, u, null);
                var other = Pick(sameCode, activity);
                return Take(ScopeMatchKinds.Activity, other, u, $"Matched to the {Label(other.Activity)} version of this item");
            }
        }

        // The wording, with and without an activity word at the start.
        var text = Norm(description);
        var tries = new List<(string Text, string Wanted)> { (text, activity) };
        foreach (var (prefix, act) in Prefixes)
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                tries.Add((text[prefix.Length..].Trim(), activity == Activities.None ? act : activity));

        // 2. Same wording and activity (a line with no activity is read as Replace).
        foreach (var (t, wanted) in tries)
            if (byText.TryGetValue(t, out var rows)
                && (rows.FirstOrDefault(r => r.Activity == wanted) ?? (wanted == Activities.None ? rows.FirstOrDefault(r => r.Activity == Activities.Replace) : null)) is { } hit)
                return Take(ScopeMatchKinds.Description, hit, u, null);

        // 3. Same wording, another activity.
        foreach (var (t, wanted) in tries)
            if (byText.TryGetValue(t, out var rows))
            {
                var other = Pick(rows, wanted);
                return Take(ScopeMatchKinds.Activity, other, u, $"Matched to the {Label(other.Activity)} version of this item");
            }

        // 4. Our wording is a cut-off version of the carrier's.
        foreach (var (t, wanted) in tries)
        {
            var best = longTexts.Where(b => t.StartsWith(b, StringComparison.Ordinal)).MaxBy(b => b.Length);
            if (best is not null) return Take(ScopeMatchKinds.Shortened, Pick(byText[best], wanted), u, "Your price list's description is shortened");
        }

        // 5. Closest wording by shared words (Jaccard), from the activity-less wording.
        var (lineText, lineWanted) = tries[^1];
        var lineTokens = Tokens(lineText);
        if (lineTokens.Length == 0) return ScopeMatch.None;
        var shared = new Dictionary<string, int>();
        foreach (var token in lineTokens)
            if (textsByToken.TryGetValue(token, out var texts) && texts.Count < 400)
                foreach (var t in texts) shared[t] = shared.GetValueOrDefault(t) + 1;
        if (shared.Count == 0) return ScopeMatch.None;
        var (bestText, score) = shared
            .Select(kv => (Text: kv.Key, Score: (decimal)kv.Value / (lineTokens.Length + tokensByText[kv.Key].Length - kv.Value)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Text, StringComparer.Ordinal).First();
        if (score < SuggestAt) return ScopeMatch.None;
        var candidate = Pick(byText[bestText], lineWanted);
        var pct = (int)Math.Round(score * 100, MidpointRounding.AwayFromZero);
        if (score >= TakeAt && (u.Length == 0 || u == candidate.Unit))
            return new ScopeMatch(ScopeMatchKinds.Close, candidate, null, pct, $"Close wording ({pct}% of words shared). Check it");
        return new ScopeMatch(null, null, candidate, pct, $"Closest item on your price list ({pct}% of words shared)");
    }

    /// <summary>A match, unless the units differ: then only a suggestion.</summary>
    private static ScopeMatch Take(string kind, PriceBookEntry e, string unit, string? note) =>
        unit.Length > 0 && e.Unit.Length > 0 && !string.Equals(unit, e.Unit, StringComparison.OrdinalIgnoreCase)
            ? new ScopeMatch(null, null, e, null, $"Your item is priced per {e.Unit}; the carrier's line is in {unit}")
            : new ScopeMatch(kind, e, null, null, note);

    /// <summary>The row for the activity wanted, else the closest one available.</summary>
    private static PriceBookEntry Pick(List<PriceBookEntry> rows, string wanted)
    {
        foreach (var a in new[] { wanted, Activities.Replace, Activities.RemoveReplace, Activities.None, Activities.Install, Activities.Remove, Activities.Detach })
            if (rows.FirstOrDefault(r => r.Activity == a) is { } r) return r;
        return rows[0];
    }

    private static string Label(string activity) => Activities.Label(activity) is { Length: > 0 } l ? l : "plain";

    /// <summary>Comparison form: lowercase, straight quotes, single spaces, no added-line asterisk.</summary>
    public static string Norm(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
        s = Regex.Replace(s, @"^\*\s*|\s*\*$", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string[] Tokens(string text) =>
        Regex.Split(text, "[^a-z0-9]+").Where(w => w.Length > 0 && !Stop.Contains(w)).Distinct().ToArray();
}
