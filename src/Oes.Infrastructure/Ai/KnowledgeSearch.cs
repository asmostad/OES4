using System.Text;
using System.Text.RegularExpressions;

namespace Oes.Infrastructure.Ai;

/// <summary>A passage cut from an article.</summary>
public sealed record KnowledgePassage(int Seq, string? Heading, string Text);

/// <summary>A passage to score: its article title and searchable words.</summary>
public sealed record KnowledgeCandidate(Guid ChunkId, Guid ArticleId, string Title, string SearchText);

/// <summary>
/// The knowledge base's search, with no external index: articles are cut into passages of about
/// <see cref="TargetChars"/> characters along paragraphs (keeping the nearest heading), and a query is
/// scored against each passage by the words it shares, weighted so rare words count more (an IDF
/// over the candidates), with a bonus when the article's title has the word.
/// </summary>
public static partial class KnowledgeSearch
{
    public const int TargetChars = 1200;
    public const int MaxQueryWords = 8;

    private static readonly HashSet<string> Stop =
    [
        "a", "an", "the", "and", "or", "of", "to", "in", "on", "for", "by", "with", "is", "are", "was", "be", "it", "its", "this", "that",
        "what", "how", "do", "does", "we", "our", "us", "you", "your", "can", "i", "my", "me", "if", "at", "as", "from", "about", "any",
    ];

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLine();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();

    /// <summary>Lowercase words, without common words, each once, in order.</summary>
    public static List<string> Words(string text) =>
        NonWord().Split(text.ToLowerInvariant()).Where(w => w.Length >= 2 && !Stop.Contains(w)).Distinct().ToList();

    /// <summary>
    /// Query words, cut back to a stem that prefixes the other forms ("warranties" → "warrant", "shingles" → "shingle"),
    /// because passages match words by prefix. At most <see cref="MaxQueryWords"/>.
    /// </summary>
    public static List<string> QueryWords(string query) =>
        Words(query).Select(w => w.Length > 5 && w.EndsWith("ies") ? w[..^3] : w.Length > 5 && w.EndsWith('y') ? w[..^1]
                : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w)
            .Distinct().Take(MaxQueryWords).ToList();

    /// <summary>What's stored for matching: the title, heading and text as single-spaced lowercase words.</summary>
    public static string SearchTextFor(string title, string? heading, string text) =>
        " " + string.Join(' ', NonWord().Split($"{title} {heading} {text}".ToLowerInvariant()).Where(w => w.Length > 0)) + " ";

    /// <summary>
    /// Cuts an article into passages: paragraphs are packed together up to the target size, a heading
    /// line (Markdown "#", or a short line ending in ":") starts a new passage, and an over-long
    /// paragraph is split at sentence ends.
    /// </summary>
    public static List<KnowledgePassage> Split(string body)
    {
        var passages = new List<KnowledgePassage>();
        string? heading = null;
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0) passages.Add(new KnowledgePassage(passages.Count, heading, current.ToString().Trim()));
            current.Clear();
        }
        foreach (var raw in BlankLine().Split(body.Replace("\r\n", "\n")))
        {
            var para = raw.Trim();
            if (para.Length == 0) continue;
            var firstLine = para.Split('\n')[0].Trim();
            var md = MarkdownHeading().Match(firstLine);
            if (md.Success || (firstLine.Length <= 80 && firstLine.EndsWith(':') && !firstLine[..^1].Contains('.')))
            {
                Flush();
                heading = md.Success ? md.Groups[2].Value.Trim() : firstLine.TrimEnd(':').Trim();
                para = para[firstLine.Length..].Trim();
                if (para.Length == 0) continue;
            }
            foreach (var piece in Pieces(para))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > TargetChars) Flush();
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }
        Flush();
        return passages;
    }

    /// <summary>A paragraph, split at sentence ends (or hard-cut) when it's longer than the target.</summary>
    private static IEnumerable<string> Pieces(string para)
    {
        if (para.Length <= TargetChars) { yield return para; yield break; }
        var sb = new StringBuilder();
        foreach (var sentence in Regex.Split(para, @"(?<=[.!?])\s+"))
        {
            var s = sentence;
            while (s.Length > TargetChars) { if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); } yield return s[..TargetChars]; s = s[TargetChars..]; }
            if (sb.Length > 0 && sb.Length + s.Length + 1 > TargetChars) { yield return sb.ToString(); sb.Clear(); }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>
    /// The best passages for the query words, at most <paramref name="perArticle"/> from one article.
    /// A passage must share at least one word; ties go to the article whose title matches.
    /// </summary>
    public static List<(KnowledgeCandidate Candidate, double Score)> Rank(IReadOnlyList<string> words, IReadOnlyList<KnowledgeCandidate> candidates,
        int take, int perArticle = 2)
    {
        if (words.Count == 0 || candidates.Count == 0) return [];
        var n = candidates.Count;
        var idf = words.ToDictionary(w => w, w =>
        {
            var df = candidates.Count(c => c.SearchText.Contains($" {w}", StringComparison.Ordinal));
            return Math.Log(1 + (n - df + 0.5) / (df + 0.5));
        });
        var scored = new List<(KnowledgeCandidate, double)>();
        foreach (var c in candidates)
        {
            double score = 0;
            var title = " " + string.Join(' ', NonWord().Split(c.Title.ToLowerInvariant())) + " ";
            foreach (var w in words)
            {
                var tf = Count(c.SearchText, $" {w}");
                if (tf == 0) continue;
                score += idf[w] * (1 + Math.Log(tf));
                if (title.Contains($" {w}", StringComparison.Ordinal)) score += idf[w] * 0.5;
            }
            if (score > 0) scored.Add((c, score));
        }
        return scored.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.Title, StringComparer.OrdinalIgnoreCase)
            .GroupBy(x => x.Item1.ArticleId).SelectMany(g => g.Take(perArticle))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.Title, StringComparer.OrdinalIgnoreCase).Take(take).ToList();
    }

    /// <summary>Occurrences of a word prefix (" shingle" also counts " shingles").</summary>
    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
