using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.NurtureManage)]
public class NurtureModel(NurtureService nurture) : CrmPageModel
{
    public List<SequenceRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await nurture.ListAsync();

    public Task<IActionResult> OnPostAsync(SequenceInput input) => Guard(async () =>
    {
        var id = await nurture.SaveAsync(null, input);
        Flash("Workflow created. Add its steps.");
        return RedirectToPage("/Admin/NurtureEdit", new { id });
    });
}
