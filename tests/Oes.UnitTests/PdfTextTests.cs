using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Oes.UnitTests;

public class PdfTextTests
{
    /// <summary>A small PDF with real text, one page per array of lines (a blank page has no lines).</summary>
    public static byte[] MakePdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.Letter);
            var y = 720.0;
            foreach (var line in lines) { page.AddText(line, 12, new PdfPoint(72, y), font); y -= 18; }
        }
        return builder.Build();
    }

    [Fact]
    public void Text_comes_out_page_by_page_in_reading_order()
    {
        var text = PdfText.Extract(MakePdf(["Workmanship warranty", "Ten years on labor."], [], ["Claims: call the office."]), 100_000);
        Assert.Contains("Workmanship warranty", text);
        Assert.True(text.IndexOf("Ten years", StringComparison.Ordinal) < text.IndexOf("Claims", StringComparison.Ordinal));
        Assert.Contains("\n\nClaims: call the office.", text); // pages are separated by a blank line; the empty page adds nothing
    }

    [Fact]
    public void Scans_damaged_files_and_huge_texts_are_refused_with_a_reason()
    {
        Assert.Contains("probably a scan", Assert.Throws<CrmRuleException>(() => PdfText.Extract(MakePdf([]), 100_000)).Message);
        Assert.Contains("couldn't be read as a PDF", Assert.Throws<CrmRuleException>(() => PdfText.Extract([1, 2, 3], 100_000)).Message);
        Assert.Contains("Split it", Assert.Throws<CrmRuleException>(() => PdfText.Extract(MakePdf(["A long enough line of text"]), 10)).Message);
    }
}
