using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Prospecting;

/// <summary>Call-list filters shared by the start page and the work page (all in the query string).</summary>
public abstract class ProspectingPageModel : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Stage { get; set; }
    [BindProperty(SupportsGet = true)] public string? Source { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Storm { get; set; }
    [BindProperty(SupportsGet = true)] public bool Mine { get; set; }

    public ProspectFilter Filter => new(Stage, string.IsNullOrWhiteSpace(Source) ? null : Source, Storm, Mine);

    public Dictionary<string, string?> FilterRoute(Guid? id = null, string? skip = null)
    {
        var r = new Dictionary<string, string?>();
        if (id is not null) r["id"] = id.ToString();
        if (Stage is not null) r["stage"] = Stage.ToString();
        if (!string.IsNullOrWhiteSpace(Source)) r["source"] = Source;
        if (Storm is not null) r["storm"] = Storm.ToString();
        if (Mine) r["mine"] = "true";
        if (!string.IsNullOrEmpty(skip)) r["skip"] = skip;
        return r;
    }
}

[HasPermission(Permissions.LeadsProspect)]
public class IndexModel(ProspectingService prospecting, CrmLookups lookups, Oes.Infrastructure.Insurance.StormService storms) : ProspectingPageModel
{
    public ProspectingService.QueueCounts Counts { get; private set; } = new(0, 0, 0);
    public List<PipelineStage> Stages { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<Oes.Infrastructure.Insurance.StormRow> Storms { get; private set; } = [];
    public List<Disposition> Dispositions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Counts = await prospecting.CountsAsync(Filter);
        Stages = await prospecting.ProspectingStagesAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Storms = await storms.ListAsync();
        Dispositions = await prospecting.DispositionsAsync();
    }
}
