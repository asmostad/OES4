using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Opportunities;

public class IndexModel(OpportunityService opportunities, CrmLookups lookups, Oes.Infrastructure.Insurance.StormService storms) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "board";
    [BindProperty(SupportsGet = true)] public Guid? Stage { get; set; }
    [BindProperty(SupportsGet = true)] public string? Outcome { get; set; }
    [BindProperty(SupportsGet = true)] public bool Mine { get; set; }
    [BindProperty(SupportsGet = true, Name = "storm")] public Guid? StormId { get; set; }
    public List<Oes.Infrastructure.Insurance.StormRow> Storms { get; private set; } = [];
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public List<BoardColumn> Board { get; private set; } = [];
    public PagedResult<OpportunityRow> List { get; private set; } = new([], 0, 1, 25);
    public List<PipelineStage> Stages { get; private set; } = [];
    public List<Option> LostReasons { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Stages = await opportunities.StagesAsync();
        LostReasons = await lookups.ValuesAsync(LookupTypes.LostReason);
        Storms = await storms.ListAsync();
        if (View == "list") List = await opportunities.SearchAsync(Q, Stage, Outcome, Mine, PageNumber, 25, default, StormId);
        else Board = await opportunities.BoardAsync(Q, Mine, default, StormId);
    }
}
