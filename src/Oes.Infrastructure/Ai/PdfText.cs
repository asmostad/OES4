using System.Text;
using System.Text.RegularExpressions;
using Oes.Infrastructure.Crm;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Oes.Infrastructure.Ai;

/// <summary>
/// The text of a PDF (warranty terms, spec sheets) for the knowledge base, read locally with PdfPig: no AI call,
/// nothing leaves the server. Pages are read in reading order and separated by a blank line. A scanned PDF (pictures
/// of pages, no text layer) has nothing to read and is refused with a note saying so.
/// </summary>
public static partial class PdfText
{
    public const long MaxBytes = 20 * 1024 * 1024;

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    public static string Extract(byte[] pdf, int maxChars)
    {
        if (pdf.Length > MaxBytes) throw new CrmRuleException("That PDF is over 20 MB.");
        var text = new StringBuilder();
        try
        {
            using var doc = PdfDocument.Open(pdf);
            foreach (var page in doc.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText)) continue;
                if (text.Length > 0) text.Append("\n\n");
                text.Append(pageText);
                if (text.Length > maxChars) break;
            }
        }
        catch (Exception ex) when (ex is not CrmRuleException)
        {
            throw new CrmRuleException("That file couldn't be read as a PDF (it may be damaged or password-protected).");
        }
        var clean = BlankLines().Replace(string.Join('\n', text.ToString().Replace("\r\n", "\n").Split('\n').Select(l => Spaces().Replace(l, " ").TrimEnd())), "\n\n").Trim();
        if (clean.Length == 0) throw new CrmRuleException("That PDF has no text to read. It's probably a scan: paste or type the text instead.");
        if (clean.Length > maxChars) throw new CrmRuleException($"That PDF has more than {maxChars:#,0} characters of text. Split it into a few articles.");
        return clean;
    }
}
