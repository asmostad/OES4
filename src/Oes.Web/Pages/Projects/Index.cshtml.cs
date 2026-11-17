using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

public class IndexModel(ProjectService projects) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "board";
    [BindProperty(SupportsGet = true)] public bool Mine { get; set; }
    [BindProperty(SupportsGet = true)] public bool Removed { get; set; }

    public List<BoardColumn<ProjectRow>> Board { get; private set; } = [];
    public List<ProjectRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (View == "list" || Removed) { View = "list"; Rows = await projects.ListAsync(Q, null, Removed, Mine); }
        else Board = await projects.ProjectBoardAsync(Q, Mine);
    }
}
