using Microsoft.AspNetCore.Mvc.RazorPages;
using Oes.Application.Security;
using Oes.Infrastructure.Admin;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.RolesManage)]
public class RolesModel(TenantAdminService admin) : PageModel
{
    public List<RoleRow> Roles { get; private set; } = [];

    public async Task OnGetAsync() => Roles = await admin.RolesAsync();
}
