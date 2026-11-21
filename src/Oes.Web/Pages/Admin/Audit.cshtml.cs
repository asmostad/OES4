using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Infrastructure.Persistence;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.AuditView)]
public class AuditModel(OesDbContext db) : PageModel
{
    public const int PageSize = 100;

    public sealed record Row(DateTime At, string? User, string Action, string? EntityType, string? Summary, string? Ip);

    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public List<Row> Rows { get; private set; } = [];
    public List<string> Actions { get; private set; } = [];
    public bool HasMore { get; private set; }

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(1, PageNumber);
        Actions = await db.AuditEntries.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

        var q = db.AuditEntries.AsQueryable();
        if (!string.IsNullOrEmpty(Action)) q = q.Where(a => a.Action == Action);

        var rows = await (from a in q
                          join u in db.Users on a.UserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          orderby a.CreatedAt descending
                          select new Row(a.CreatedAt, u == null ? null : u.FirstName + " " + u.LastName, a.Action, a.EntityType, a.Summary, a.IpAddress))
            .Skip((PageNumber - 1) * PageSize).Take(PageSize + 1).ToListAsync();

        HasMore = rows.Count > PageSize;
        Rows = rows.Take(PageSize).ToList();
    }
}
