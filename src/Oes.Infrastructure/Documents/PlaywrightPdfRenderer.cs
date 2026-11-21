using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Documents;

public sealed class PdfOptions
{
    public const string Section = "Pdf";

    /// <summary>The installed browser to use: "msedge" or "chrome". Blank tries Edge then Chrome on Windows, Chrome then Edge elsewhere (Edge can hang starting on Linux).</summary>
    public string? Channel { get; set; }

    /// <summary>A Chromium-based browser's executable, instead of an installed channel.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>How many documents render at once.</summary>
    public int MaxConcurrent { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// HTML to PDF in headless Chromium (Playwright), using a browser already on the machine (Edge on
/// Windows Server, Edge or Chrome on Linux), so nothing is downloaded. The page can't reach anything:
/// JavaScript is off, every request is aborted, and the context is offline, so documents must be
/// self-contained. One browser is kept for the app's lifetime and relaunched if it goes away; each
/// document gets its own context.
/// </summary>
public sealed class PlaywrightPdfRenderer(IOptions<PdfOptions> options, ILogger<PlaywrightPdfRenderer> log) : IPdfRenderer, IAsyncDisposable
{
    private readonly PdfOptions o = options.Value;
    private readonly SemaphoreSlim launch = new(1, 1);
    private readonly SemaphoreSlim slots = new(Math.Max(1, options.Value.MaxConcurrent));
    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task<byte[]> RenderAsync(string html, string? footer = null, CancellationToken ct = default)
    {
        await slots.WaitAsync(ct);
        try
        {
            var b = await BrowserAsync(ct);
            await using var context = await b.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false, Offline = true });
            await context.RouteAsync("**/*", route => route.AbortAsync());
            var page = await context.NewPageAsync();
            var timeout = o.TimeoutSeconds * 1000f;
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load, Timeout = timeout });
            ct.ThrowIfCancellationRequested();
            return await page.PdfAsync(new PagePdfOptions
            {
                Format = "Letter",
                PrintBackground = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<span></span>",
                FooterTemplate = Footer(footer),
                Margin = new Margin { Top = "0.5in", Bottom = "0.6in", Left = "0.5in", Right = "0.5in" },
            });
        }
        finally { slots.Release(); }
    }

    /// <summary>The page footer: the given text on the left, "Page n of m" on the right (Chromium fills the page classes).</summary>
    internal static string Footer(string? text) =>
        "<div style=\"font-family:Arial,sans-serif;font-size:7pt;color:#94a3b8;width:100%;padding:0 0.5in;display:flex;justify-content:space-between\">"
        + $"<span>{WebUtility.HtmlEncode(text ?? "")}</span>"
        + "<span>Page <span class=\"pageNumber\"></span> of <span class=\"totalPages\"></span></span></div>";

    /// <summary>Installed browsers to try, in order: Edge first on Windows (it ships there), Chrome first elsewhere.</summary>
    internal static string?[] DefaultChannels => OperatingSystem.IsWindows() ? ["msedge", "chrome"] : ["chrome", "msedge"];

    private async Task<IBrowser> BrowserAsync(CancellationToken ct)
    {
        if (browser is { IsConnected: true } ready) return ready;
        await launch.WaitAsync(ct);
        try
        {
            if (browser is { IsConnected: true } b) return b;
            if (browser is not null) { try { await browser.DisposeAsync(); } catch (PlaywrightException) { } }
            playwright ??= await Playwright.CreateAsync();
            string?[] attempts = !string.IsNullOrWhiteSpace(o.ExecutablePath) ? [null]
                : !string.IsNullOrWhiteSpace(o.Channel) ? [o.Channel] : DefaultChannels;
            Exception? last = null;
            foreach (var channel in attempts)
            {
                try
                {
                    browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true, Channel = channel,
                        ExecutablePath = string.IsNullOrWhiteSpace(o.ExecutablePath) ? null : o.ExecutablePath,
                        Timeout = o.TimeoutSeconds * 1000f,
                    });
                    log.LogInformation("PDF renderer started {Browser} {Version}", channel ?? o.ExecutablePath, browser.Version);
                    return browser;
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { last = ex; } // try the next browser
            }
            log.LogError(last, "No browser could be started for PDFs");
            throw new PdfUnavailableException("PDFs can't be made on this server: no Edge or Chrome browser was found. Set Pdf:ExecutablePath, or print from the browser instead.", last);
        }
        finally { launch.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null) { try { await browser.DisposeAsync(); } catch (PlaywrightException) { } }
        playwright?.Dispose();
    }
}
