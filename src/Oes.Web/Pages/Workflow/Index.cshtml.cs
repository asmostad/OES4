using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Workflow;

/// <summary>The production board: every trade across projects, by trade stage.</summary>
public class IndexModel(ProjectService projects, MeasurementService measurements) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Trade { get; set; }
    [BindProperty(SupportsGet = true)] public bool Mine { get; set; }

    public List<BoardColumn<TradeCard>> Board { get; private set; } = [];
    public List<TradeType> Trades { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Trades = await measurements.TradesAsync();
        Board = await projects.TradeBoardAsync(Trade, Mine);
    }
}
