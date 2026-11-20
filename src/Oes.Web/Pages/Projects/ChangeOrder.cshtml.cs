using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

/// <summary>
/// A change order: its items while it's a draft, the customer's decision, a printable copy (?print=true), its PDF, and
/// emailing that PDF to the customer (?handler=Email).
/// </summary>
public class ChangeOrderModel(ProjectService projects, OesDbContext db, ITenantContext tenant, Oes.Infrastructure.Communication.MessagingService messaging,
    Oes.Infrastructure.Crm.RecordItems items, ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Print { get; set; }

    public ChangeOrderDetail Detail { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    [BindProperty] public DocumentEmailInput Mail { get; set; } = new();
    /// <summary>The email panel on the print view, when the user may send email.</summary>
    public DocumentEmailPanel? EmailPanel { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await projects.GetChangeOrderAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        if (Print && await user.HasPermissionAsync(Oes.Application.Security.Permissions.EmailSend))
        {
            var recipients = (await messaging.RecipientsAsync(Oes.Domain.Crm.EntityTypes.Project, Detail.Project.Id))
                .Where(r => Oes.Infrastructure.Communication.ConsentService.KindsFor(Oes.Domain.Communication.Channels.Email).Contains(r.Kind)).ToList();
            EmailPanel = new DocumentEmailPanel
            {
                Title = "Email to the customer", Handler = "Email", AttachmentName = PdfDocument.FileName(PdfName), Recipients = recipients,
                NoRecipients = "The customer has no email address on file. Add one on their contact page first.",
                Mail = new DocumentEmailInput
                {
                    To = recipients.Where(r => r.IsPrimary).Select(r => $"{r.ContactId}|{r.Address}").FirstOrDefault(),
                    Subject = $"Change order #{Detail.ChangeOrder.Sequence} for {Detail.Project.Number} from {Company.Name}",
                    Body = $"Hi {Detail.CustomerName.Split(' ')[0]},\n\nAttached is change order #{Detail.ChangeOrder.Sequence}: {Detail.ChangeOrder.Description}. "
                           + "Please review it and let us know if you have any questions.\n\nThanks",
                },
            };
        }
        return Page();
    });

    private string PdfName => $"{Detail.Project.Number} change order {Detail.ChangeOrder.Sequence}";

    /// <summary>Renders the change order's PDF, saves it on the project, and emails it to the chosen customer address.</summary>
    public Task<IActionResult> OnPostEmailAsync() => Guard(async () =>
    {
        Detail = await projects.GetChangeOrderAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        var printUrl = $"/Projects/ChangeOrder/{Id}?print=true";
        if (Mail.ContactPick() is not { } pick) { Flash("Pick who to send it to.", "danger"); return Redirect(printUrl); }
        Print = true;
        ViewData[PdfDocument.ViewDataKey] = true;
        return new PdfPageResult(PdfName, $"{Company.Name} · {PdfName}", printUrl)
        {
            Deliver = async pdf =>
            {
                using var stream = new MemoryStream(pdf);
                var doc = await items.UploadAsync(Oes.Domain.Crm.EntityTypes.Project, Detail.Project.Id, stream, PdfDocument.FileName(PdfName), "application/pdf", "change_order");
                var sent = await messaging.SendEmailAsync(new Oes.Infrastructure.Communication.EmailInput
                {
                    EntityType = Oes.Domain.Crm.EntityTypes.Project, EntityId = Detail.Project.Id, ContactId = pick.ContactId, To = pick.Address,
                    Cc = Mail.Cc, Bcc = Mail.Bcc, Subject = Mail.Subject ?? "", Body = Mail.Body ?? "", DocumentIds = [doc],
                });
                if (!sent.Ok) return $"The PDF is saved on the project, but the email didn't go through: {sent.Error}";
                await items.LogAsync(Oes.Domain.Crm.EntityTypes.Project, Detail.Project.Id, "change_order.emailed",
                    $"Emailed change order #{Detail.ChangeOrder.Sequence} to {pick.Address}");
                await db.SaveChangesAsync();
                return $"Change order emailed to {pick.Address}, with the PDF attached. A copy is saved on the project.";
            },
        };
    });

    /// <summary>The printable copy as a PDF download.</summary>
    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        Detail = await projects.GetChangeOrderAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        Print = true;
        var name = $"{Detail.Project.Number} change order {Detail.ChangeOrder.Sequence}";
        return await PdfAsync(name, $"{Company.Name} · {name}", $"/Projects/ChangeOrder/{Id}?print=true");
    });

    public Task<IActionResult> OnPostPriceItemAsync(Guid priceListItemId, decimal qty) => Do(() => projects.AddChangeOrderPriceItemAsync(Id, priceListItemId, qty));

    public Task<IActionResult> OnPostProductAsync(Guid productId, decimal qty) => Do(() => projects.AddChangeOrderProductAsync(Id, productId, qty));

    public Task<IActionResult> OnPostCustomAsync(string description, decimal qty, string? unit, decimal unitPrice) =>
        Do(() => projects.AddChangeOrderCustomLineAsync(Id, description, qty, unit, unitPrice));

    public Task<IActionResult> OnPostUpdateLineAsync(Guid lineId, decimal qty, decimal unitPrice, string? description) =>
        Do(() => projects.UpdateChangeOrderLineAsync(lineId, qty, unitPrice, description));

    public Task<IActionResult> OnPostRemoveLineAsync(Guid lineId) => Do(() => projects.RemoveChangeOrderLineAsync(lineId));

    public Task<IActionResult> OnPostDecideAsync(bool approve, string? approvedByName) => Guard(async () =>
    {
        await projects.DecideChangeOrderAsync(Id, approve, approvedByName);
        Flash(approve ? "Change order approved and added to the contract." : "Change order rejected.");
        return RedirectToPage(new { id = Id });
    }, StayHere);

    private Task<IActionResult> Do(Func<Task> action) => Guard(async () =>
    {
        await action();
        return RedirectToPage(new { id = Id });
    }, StayHere);

    private IActionResult StayHere()
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(new { id = Id });
    }
}
