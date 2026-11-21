using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Booking;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.BookingManage)]
public class BookingTypesModel(BookingService booking, Oes.Infrastructure.Persistence.OesDbContext db, ITenantContext tenant, IConfiguration config) : CrmPageModel
{
    public List<BookingType> Types { get; private set; } = [];
    public string CompanySlug { get; private set; } = "";
    public string PublicUrl => $"{(config["App:BaseUrl"] ?? "").TrimEnd('/')}/book/{CompanySlug}";

    public async Task OnGetAsync()
    {
        Types = await booking.TypesAsync();
        CompanySlug = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.Slug));
    }
}
