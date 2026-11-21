using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Storms;

[HasPermission(Permissions.StormsManage)]
public class EditModel(StormService storms, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public StormInput Input { get; set; } = new();
    public List<Option> Perils { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var s = await storms.GetAsync(id);
            Input = new StormInput
            {
                Name = s.Name, EventDate = s.EventDate, PerilKey = s.PerilKey, MaxHailSizeIn = s.MaxHailSizeIn,
                MaxWindMph = s.MaxWindMph, ZipCodes = s.ZipCodes, Notes = s.Notes,
            };
        }
        Perils = await lookups.ValuesAsync(LookupTypes.Peril);
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        Perils = await lookups.ValuesAsync(LookupTypes.Peril);
        return await Guard(async () =>
        {
            var id = await storms.SaveAsync(Id, Input);
            Flash("Storm saved.");
            return RedirectToPage("/Storms/View", new { id });
        }, Page);
    }

    public Task<IActionResult> OnPostArchiveAsync() => Guard(async () =>
    {
        await storms.ArchiveAsync(Id!.Value);
        Flash("Storm archived.");
        return RedirectToPage("/Storms/Index");
    });
}
