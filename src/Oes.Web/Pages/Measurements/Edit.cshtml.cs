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
public class EditModel(MeasurementService measurements, PropertyService properties) : CrmPageModel
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
