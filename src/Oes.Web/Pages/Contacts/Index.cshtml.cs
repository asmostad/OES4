using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Contacts;

public class IndexModel(ContactService contacts) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<ContactRow> Result { get; private set; } = new([], 0, 1, 25);

    public async Task OnGetAsync() => Result = await contacts.SearchAsync(Q, Type, Status, PageNumber);
}
