using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oes.Application.Security;
using Oes.Infrastructure.Admin;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.RolesManage)]
public class RoleEditModel(TenantAdminService admin) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty, Required, StringLength(100)] public string Name { get; set; } = "";
    [BindProperty, StringLength(500)] public string? Description { get; set; }
    [BindProperty] public List<string> PermissionKeys { get; set; } = [];

    public bool IsSystem { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is not { } id) return Page();
        try
        {
            var role = await admin.GetRoleAsync(id);
            Name = role.Name;
            Description = role.Description;
            IsSystem = role.IsSystem;
            PermissionKeys = role.Permissions.Select(p => p.PermissionKey).ToList();
            return Page();
        }
        catch (AdminRuleException) { return NotFound(); }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            await admin.SaveRoleAsync(Id, Name, Description, PermissionKeys);
            TempData["Flash"] = $"Role \"{Name}\" saved.";
            return RedirectToPage("/Admin/Roles");
        }
        catch (AdminRuleException ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is not { } id) return RedirectToPage("/Admin/Roles");
        try
        {
            await admin.DeleteRoleAsync(id);
            TempData["Flash"] = "Role deleted.";
            return RedirectToPage("/Admin/Roles");
        }
        catch (AdminRuleException ex)
        {
            TempData["Flash"] = ex.Message;
            TempData["FlashType"] = "danger";
            return RedirectToPage(new { id });
        }
    }
}
