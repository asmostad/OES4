using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Net.Http.Headers;
using Oes.Application.Abstractions;

namespace Oes.Web.Infrastructure;

/// <summary>
/// The page's own view, run into a buffer instead of the response, then printed to PDF and sent as a
/// download. It's a <see cref="PageResult"/> so Razor Pages attaches the page and view data as usual.
/// </summary>
public sealed class PdfPageResult(string fileName, string? footer, string printUrl) : PageResult
{
    /// <summary>
    /// Instead of downloading: hands the PDF to this (to save and email it), then goes back to the print view
    /// with the message it returns. A rule or permission error is shown there instead.
    /// </summary>
    public Func<byte[], Task<string>>? Deliver { get; init; }

    /// <summary>
    /// With <see cref="Deliver"/>: what to do when no PDF can be made (no browser on the server), for steps that must
    /// still happen (a work order's email goes out without the attachment). Its message is shown with the reason.
    /// </summary>
    public Func<Task<string>>? WithoutPdf { get; init; }

    private static void Flash(HttpContext http, string message, string type)
    {
        var tempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        tempData["Flash"] = message;
        tempData["FlashType"] = type;
    }

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var body = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try { await base.ExecuteResultAsync(context); }
        finally { http.Response.Body = body; }
        var html = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        byte[] pdf;
        try { pdf = await http.RequestServices.GetRequiredService<IPdfRenderer>().RenderAsync(html, footer, http.RequestAborted); }
        catch (PdfUnavailableException ex)
        {
            if (WithoutPdf is not null)
            {
                try { Flash(http, $"{await WithoutPdf()} No PDF was attached: {ex.Message}", "warning"); }
                catch (Exception inner) when (inner is Oes.Infrastructure.Crm.CrmRuleException or Oes.Infrastructure.Crm.RecordForbiddenException) { Flash(http, inner.Message, "danger"); }
            }
            else Flash(http, ex.Message, "danger");
            await new LocalRedirectResult(printUrl).ExecuteResultAsync(context);
            return;
        }
        if (Deliver is not null)
        {
            try { Flash(http, await Deliver(pdf), "success"); }
            catch (Exception ex) when (ex is Oes.Infrastructure.Crm.CrmRuleException or Oes.Infrastructure.Crm.RecordForbiddenException)
            {
                Flash(http, ex.Message, "danger");
            }
            await new LocalRedirectResult(printUrl).ExecuteResultAsync(context);
            return;
        }
        http.Response.ContentType = "application/pdf";
        http.Response.ContentLength = pdf.Length;
        http.Response.Headers[HeaderNames.ContentDisposition] =
            new ContentDispositionHeaderValue("attachment") { FileName = PdfDocument.FileName(fileName) }.ToString();
        await http.Response.Body.WriteAsync(pdf, http.RequestAborted);
    }
}

/// <summary>
/// Server-side PDFs of print documents (proposal, work order, change order, order sheet). In PDF mode
/// the print layout can't load anything, so it inlines <see cref="StyleFiles"/> instead of linking the
/// CDN and app stylesheets.
/// </summary>
public static class PdfDocument
{
    public const string ViewDataKey = "PdfMode";

    /// <summary>The Bootstrap subset the documents use, then the document styles. Paths under wwwroot.</summary>
    public static readonly string[] StyleFiles = ["css/pich-document.css", "css/oes4-print.css"];

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static bool IsPdf(Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData) => viewData[ViewDataKey] is true;

    /// <summary>The inline CSS for PDF mode, read once from wwwroot.</summary>
    public static string Styles(IWebHostEnvironment env) => Cache.GetOrAdd(env.WebRootPath ?? "", _ =>
        string.Join("\n", StyleFiles.Select(f => env.WebRootFileProvider.GetFileInfo(f)).Where(f => f.Exists)
            .Select(f => { using var r = new StreamReader(f.CreateReadStream()); return r.ReadToEnd(); }))
        .Replace("</", "<\\/"));

    /// <summary>A safe download name: letters, digits, dots, dashes and underscores, ending in .pdf.</summary>
    public static string FileName(string name)
    {
        var clean = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-', '.');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        if (clean.Length == 0) clean = "document";
        if (clean.Length > 100) clean = clean[..100];
        return clean.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? clean : clean + ".pdf";
    }
}
