using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Admin;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.UsersManage)]
public class UsersModel(TenantAdminService admin, ICurrentUser me, IConfiguration config) : PageModel
{
    public List<MemberRow> Members { get; private set; } = [];
    public List<RoleRow> Roles { get; private set; } = [];
    public Guid? MyUserId => me.UserId;

    public sealed class InviteInput
    {
        [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
        [Required, StringLength(100), Display(Name = "First name")] public string FirstName { get; set; } = "";
        [Required, StringLength(100), Display(Name = "Last name")] public string LastName { get; set; } = "";
        [MinLength(1, ErrorMessage = "Choose at least one role.")] public List<Guid> RoleIds { get; set; } = [];
    }

    [BindProperty] public InviteInput Invite { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostInviteAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            ViewData["ShowInvite"] = true;
            return Page();
        }
        return await Run(() => admin.InviteAsync(
            new InviteRequest(Invite.Email, Invite.FirstName, Invite.LastName, Invite.RoleIds),
            config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is not configured.")),
            $"Invitation sent to {Invite.Email}.");
    }

    public Task<IActionResult> OnPostRolesAsync(Guid membershipId, List<Guid> roleIds) =>
        Run(() => admin.SetMemberRolesAsync(membershipId, roleIds), "Roles updated.");

    public Task<IActionResult> OnPostStatusAsync(Guid membershipId, MembershipStatus status) =>
        Run(() => admin.SetMemberStatusAsync(membershipId, status),
            status == MembershipStatus.Active ? "User enabled." : "User disabled.");

    private async Task<IActionResult> Run(Func<Task> action, string success)
    {
        try
        {
            await action();
            TempData["Flash"] = success;
        }
        catch (AdminRuleException ex)
        {
            TempData["Flash"] = ex.Message;
            TempData["FlashType"] = "danger";
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Members = await admin.MembersAsync();
        Roles = await admin.RolesAsync();
    }
}
