using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Tasks;

public class IndexModel(WorkService work, CrmLookups lookups, ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public bool All { get; set; }
    [BindProperty(SupportsGet = true)] public bool Done { get; set; }

    public List<TaskRow> Tasks { get; private set; } = [];
    public List<Option> Users { get; private set; } = [];
    public Guid? Me => user.UserId;

    public async Task OnGetAsync()
    {
        Tasks = await work.TasksAsync(onlyMine: !All, includeDone: Done);
        Users = await lookups.UsersAsync();
    }
}
