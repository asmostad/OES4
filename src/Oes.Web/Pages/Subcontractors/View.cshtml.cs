using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Subcontractors;

[RequestSizeLimit(30 * 1024 * 1024)]
public class ViewModel(SubcontractorService subs, WorkOrderService workOrders) : CrmPageModel
{
    private static readonly FileExtensionContentTypeProvider Types = new();

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public SubDetail Detail { get; private set; } = null!;
    public List<WorkOrderRow> WorkOrders { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await subs.GetAsync(Id);
        WorkOrders = await workOrders.ListAsync(null, Id, null);
        return Page();
    });

    private Task<IActionResult> Do(Func<Task> action, string message) => Guard(async () =>
    {
        await action();
        Flash(message);
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostProfileAsync(List<string> tradeCodes, string status, string? notes) =>
        Do(() => subs.UpdateProfileAsync(Id, tradeCodes, status, notes), "Profile saved.");

    public Task<IActionResult> OnPostRateAsync(Guid? tradeTypeId, string description, string unit, decimal rate, DateOnly effectiveFrom) =>
        Do(() => subs.AddRateAsync(Id, tradeTypeId, description, unit, rate, effectiveFrom), "Rate added.");

    public Task<IActionResult> OnPostDocumentAsync(string docTypeKey, DateOnly? expiresOn, IFormFile? file) => Do(async () =>
    {
        if (file is null || file.Length == 0) throw new CrmRuleException("Choose a file.");
        var type = Types.TryGetContentType(file.FileName, out var t) ? t : "application/octet-stream";
        await using var stream = file.OpenReadStream();
        try { await subs.AddDocumentAsync(Id, docTypeKey, expiresOn, stream, file.FileName, type); }
        catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }
    }, "Document uploaded.");
}
