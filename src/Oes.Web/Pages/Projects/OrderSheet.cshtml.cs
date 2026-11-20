using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

/// <summary>
/// A trade's materials order: fill from the takeoff or the sold estimate, edit, print for the supplier (?print=true),
/// email its PDF to the supplier (?handler=Email, optionally marking it ordered), mark ordered.
/// </summary>
public class OrderSheetModel(OrderSheetService orders, OesDbContext db, ITenantContext tenant, Oes.Infrastructure.Communication.MessagingService messaging,
    Oes.Infrastructure.Crm.RecordItems items, ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Print { get; set; }
    [BindProperty] public TakeoffChoicesInput Choices { get; set; } = new();
    [BindProperty] public DocumentEmailInput Mail { get; set; } = new();

    public OrderSheet Sheet { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;
    public bool Previewed { get; private set; }
    /// <summary>The email panel on the print view, when the user may send email.</summary>
    public DocumentEmailPanel? EmailPanel { get; private set; }

    private string PdfName => $"{Sheet.Trade.Project.Number} {Sheet.Trade.Type.Name} order";

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        if (Print && await user.HasPermissionAsync(Oes.Application.Security.Permissions.EmailSend))
        {
            var t = Sheet.Trade;
            EmailPanel = new DocumentEmailPanel
            {
                Title = "Email to the supplier", Handler = "Email", AttachmentName = PdfDocument.FileName(PdfName), OfferMarkOrdered = t.Trade.OrderedAt is null,
                Mail = new DocumentEmailInput
                {
                    Subject = $"Material order {t.Project.Number}{(t.Property is null ? "" : " – " + t.Property.SingleLine)}",
                    Body = $"Hi,\n\nPlease see the attached {t.Type.Name.ToLowerInvariant()} material order for {t.Project.Number}"
                           + $"{(t.Property is null ? "" : " at " + t.Property.SingleLine)}. Reply to confirm the delivery date.\n\nThanks",
                    MarkOrdered = t.Trade.OrderedAt is null, Supplier = t.Trade.OrderSupplier,
                },
            };
        }
        return Page();
    });

    /// <summary>The supplier copy as a PDF download.</summary>
    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        Print = true;
        return await PdfAsync(PdfName, $"{Company.Name} · {PdfName}", $"/Projects/OrderSheet/{Id}?print=true");
    });

    /// <summary>Renders the order's PDF, saves it on the trade, emails it to the supplier's address, and can mark it ordered.</summary>
    public Task<IActionResult> OnPostEmailAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        var printUrl = $"/Projects/OrderSheet/{Id}?print=true";
        if (Sheet.Lines.Count == 0) { Flash("The order sheet is empty. Add materials before sending it.", "danger"); return Redirect(printUrl); }
        if (Mail.MarkOrdered && string.IsNullOrWhiteSpace(Mail.Supplier)) { Flash("Enter the supplier's name to mark it ordered.", "danger"); return Redirect(printUrl); }
        Print = true;
        ViewData[PdfDocument.ViewDataKey] = true;
        return new PdfPageResult(PdfName, $"{Company.Name} · {PdfName}", printUrl)
        {
            Deliver = async pdf =>
            {
                using var stream = new MemoryStream(pdf);
                var doc = await items.UploadAsync(Oes.Domain.Crm.EntityTypes.Trade, Id, stream, PdfDocument.FileName(PdfName), "application/pdf", "order_sheet");
                var sent = await messaging.SendToAddressAsync(new Oes.Infrastructure.Communication.AddressEmailInput
                {
                    EntityType = Oes.Domain.Crm.EntityTypes.Trade, EntityId = Id, To = Mail.To ?? "", Cc = Mail.Cc, Bcc = Mail.Bcc,
                    Subject = Mail.Subject ?? "", Body = Mail.Body ?? "", DocumentIds = [doc],
                });
                if (!sent.Ok) return $"The PDF is saved on the trade, but the email didn't go through: {sent.Error}";
                if (Mail.MarkOrdered) await orders.MarkOrderedAsync(Id, true, Mail.Supplier, $"Emailed to {Mail.To?.Trim()}");
                return $"Order emailed to {Mail.To?.Trim()}{(Mail.MarkOrdered ? " and marked ordered" : "")}. A copy of the PDF is saved on the trade.";
            },
        };
    });

    public Task<IActionResult> OnPostPreviewAsync() => Guard(async () =>
    {
        await LoadAsync(Choices);
        Previewed = true;
        return Page();
    });

    public Task<IActionResult> OnPostTakeoffAsync() => Done(async () =>
    {
        var n = await orders.FillFromTakeoffAsync(Id, Choices);
        Flash($"Added {n} line{(n == 1 ? "" : "s")} from the takeoff. Lines you added or changed were left alone.");
    });

    public Task<IActionResult> OnPostEstimateAsync() => Done(async () =>
    {
        var n = await orders.CopyFromEstimateAsync(Id);
        Flash($"Copied {n} line{(n == 1 ? "" : "s")} from the sold estimate.");
    });

    public Task<IActionResult> OnPostAddAsync(Guid? productId, string? description, decimal qty, string? unit, string? notes) =>
        Done(() => orders.AddLineAsync(Id, productId, description, qty, unit, notes));

    public Task<IActionResult> OnPostUpdateAsync(Guid lineId, decimal qty, string? notes) => Done(() => orders.UpdateLineAsync(lineId, qty, notes));

    public Task<IActionResult> OnPostRemoveAsync(Guid lineId) => Done(() => orders.RemoveLineAsync(lineId));

    public Task<IActionResult> OnPostOrderedAsync(bool ordered, string? supplier, string? orderNotes) => Done(async () =>
    {
        await orders.MarkOrderedAsync(Id, ordered, supplier, orderNotes);
        Flash(ordered ? "Marked as ordered." : "Order reopened.");
    });

    private Task<IActionResult> Done(Func<Task> action) => Guard(async () =>
    {
        await action();
        return RedirectToPage(new { id = Id });
    }, () =>
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(new { id = Id });
    });

    private async Task LoadAsync(TakeoffChoicesInput? choices)
    {
        Sheet = await orders.GetAsync(Id, choices);
        Choices = Sheet.Takeoff.Choices;
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
    }
}
