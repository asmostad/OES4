using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

public class ViewModel(EstimateService estimates, MeasurementService measurements, PropertyService properties, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public EstimateDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<TradeType> Trades { get; private set; } = [];
    public List<PropertyStructure> Structures { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(string? tab) => Guard(async () =>
    {
        Detail = await estimates.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Estimate, Id, tab, null);
        Trades = await measurements.TradesAsync();
        if (Detail.Property is not null) Structures = (await properties.GetAsync(Detail.Property.Id)).Structures;
        return Page();
    });

    private Task<IActionResult> Do(Func<Task> action, string? message = null, string? anchor = null) => Guard(async () =>
    {
        await action();
        if (message is not null) Flash(message);
        return Redirect(Url.Page("/Estimates/View", new { id = Id }) + (anchor is null ? "" : "#" + anchor));
    });

    public Task<IActionResult> OnPostSettingsAsync(EstimateSettingsInput settings) => Do(() => estimates.UpdateSettingsAsync(Id, settings), "Settings saved.");

    public Task<IActionResult> OnPostAddSectionAsync(string name, Guid? structureId, Guid? tradeTypeId) =>
        Do(() => estimates.AddSectionAsync(Id, name, structureId, tradeTypeId), "Section added.");

    public Task<IActionResult> OnPostSectionAsync(Guid sectionId, string name, string? scopeOfWork) =>
        Do(() => estimates.UpdateSectionAsync(sectionId, name, scopeOfWork), "Section saved.", $"s-{sectionId:N}");

    public Task<IActionResult> OnPostDeleteSectionAsync(Guid sectionId) => Do(() => estimates.DeleteSectionAsync(sectionId), "Section removed.");

    public Task<IActionResult> OnPostAddPriceItemAsync(Guid sectionId, Guid? priceListItemId, decimal qty) => Do(async () =>
    {
        if (priceListItemId is not { } pli) throw new CrmRuleException("Pick an item from the list first.");
        await estimates.AddPriceBookLineAsync(Id, sectionId, pli, qty);
    }, null, $"s-{sectionId:N}");

    public Task<IActionResult> OnPostAddProductAsync(Guid sectionId, Guid? productId, decimal qty) => Do(async () =>
    {
        if (productId is not { } pid) throw new CrmRuleException("Pick a product first.");
        await estimates.AddProductLineAsync(Id, sectionId, pid, qty);
    }, null, $"s-{sectionId:N}");

    public Task<IActionResult> OnPostAddMiscAsync(Guid sectionId, string description, decimal qty, string unit, decimal unitPrice, bool isMaterial) =>
        Do(() => estimates.AddMiscLineAsync(Id, sectionId, description, qty, unit, unitPrice, isMaterial), "Custom line added. It needs a price approval before the estimate can be issued.", $"s-{sectionId:N}");

    public Task<IActionResult> OnPostLineAsync(Guid lineId, Guid sectionId, decimal qty, decimal unitPrice, string? description, string? notes) =>
        Do(() => estimates.UpdateLineAsync(lineId, qty, unitPrice, description, notes), null, $"s-{sectionId:N}");

    public Task<IActionResult> OnPostDeleteLineAsync(Guid lineId, Guid sectionId) => Do(() => estimates.DeleteLineAsync(lineId), null, $"s-{sectionId:N}");

    public Task<IActionResult> OnPostDecideAsync(Guid lineId, bool approve, string? note) =>
        Do(() => estimates.DecideAsync(lineId, approve, note), approve ? "Price approved." : "Price rejected.");

    public Task<IActionResult> OnPostIssueAsync(string? revisionReason) => Guard(async () =>
    {
        var versionId = await estimates.IssueAsync(Id, revisionReason);
        Flash("Estimate issued. This version is now frozen.");
        return RedirectToPage("/Estimates/Proposal", new { id = versionId });
    });

    public Task<IActionResult> OnPostSignAsync(Guid versionId, string signerName) =>
        Do(() => estimates.MarkSignedAsync(versionId, signerName), "Marked signed. The opportunity is now won.");

    public Task<IActionResult> OnPostDeclineAsync(Guid versionId, string? reason) => Do(() => estimates.MarkDeclinedAsync(versionId, reason), "Marked declined.");
}
