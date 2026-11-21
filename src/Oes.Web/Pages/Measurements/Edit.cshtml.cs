using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Measurements;

/// <summary>Enter a new version of a structure's measurements for one trade, prefilled from the current version.</summary>
[HasPermission(Permissions.MeasurementsManage)]
[RequestSizeLimit(60 * 1024 * 1024)]
public class EditModel(MeasurementService measurements, PropertyService properties, Oes.Infrastructure.Ai.MeasurementExtractor extractor) : CrmPageModel
{
    private static readonly FileExtensionContentTypeProvider Types = new();

    [BindProperty(SupportsGet = true)] public Guid PropertyId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid StructureId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid TradeTypeId { get; set; }
    [BindProperty] public MeasurementInput Input { get; set; } = new();
    [BindProperty] public IFormFile? Report { get; set; }

    public Property Property { get; private set; } = null!;
    public PropertyStructure Structure { get; private set; } = null!;
    public TradeType Trade { get; private set; } = null!;
    public List<MeasurementFieldDef> Fields { get; private set; } = [];
    public MeasurementSet? Current { get; private set; }
    public bool AiReady => extractor.IsConfigured;
    /// <summary>Fields the AI filled in (highlighted until a person saves).</summary>
    public HashSet<string> AiFilled { get; private set; } = [];
    public string? AiNotes { get; private set; }
    [BindProperty] public string? ReportFileName { get; set; }

    /// <summary>Reads the attached report with AI and pre-fills the form. Nothing is saved but the report itself.</summary>
    public async Task<IActionResult> OnPostExtractAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Report is not { Length: > 0 })
        {
            ModelState.AddModelError("", "Attach the report PDF first.");
            return Page();
        }
        return await Guard(async () =>
        {
            await using var stream = Report.OpenReadStream();
            var x = await extractor.ExtractAsync(PropertyId, StructureId, TradeTypeId, stream, Report.FileName);
            ModelState.Clear(); // show the AI's values, not the posted ones
            Input.Values = x.Values.ToDictionary(v => v.Key, v => (decimal?)v.Value);
            Input.Source = x.Source ?? Input.Source;
            Input.MeasuredOn = x.MeasuredOn ?? Input.MeasuredOn;
            Input.ReportDocumentId = x.DocumentId;
            Input.ExtractionJson = x.RawJson;
            Input.Notes = $"Read by AI from {x.FileName}." + (x.Notes is null ? "" : " " + x.Notes);
            ReportFileName = x.FileName;
            AiFilled = [.. x.Values.Keys];
            AiNotes = x.Notes;
            return Page();
        }, Page);
    }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (!await LoadAsync()) return NotFound();
        if (Current is not null)
        {
            foreach (var v in Current.Values) Input.Values[$"{v.SurfaceKey}|{v.FieldKey}"] = v.Value;
            Input.Source = Current.Source;
        }
        Input.MeasuredOn = DateOnly.FromDateTime(DateTime.UtcNow);
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        return await Guard(async () =>
        {
            (Stream, string, string)? report = null;
            Stream? stream = null;
            try
            {
                if (Report is { Length: > 0 })
                {
                    stream = Report.OpenReadStream();
                    report = (stream, Report.FileName, Types.TryGetContentType(Report.FileName, out var t) ? t : "application/octet-stream");
                }
                Guid id;
                try { id = await measurements.SaveVersionAsync(PropertyId, StructureId, TradeTypeId, Input, report); }
                catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }
                Flash($"{Trade.Name} measurements saved.");
                return RedirectToPage("/Measurements/Set", new { id });
            }
            finally
            {
                if (stream is not null) await stream.DisposeAsync();
            }
        }, Page);
    }

    private async Task<bool> LoadAsync()
    {
        var detail = await properties.GetAsync(PropertyId);
        Property = detail.Property;
        var structure = detail.Structures.FirstOrDefault(s => s.Id == StructureId);
        var trade = (await measurements.TradesAsync()).FirstOrDefault(t => t.Id == TradeTypeId);
        if (structure is null || trade is null) return false;
        Structure = structure;
        Trade = trade;
        Fields = await measurements.FieldsAsync(TradeTypeId);
        Current = await measurements.CurrentAsync(StructureId, TradeTypeId);
        return true;
    }
}
