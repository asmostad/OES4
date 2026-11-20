using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>
/// The proposal, rendered from a frozen version (never live data), behind sign-in. Print from the
/// browser, download the server-made PDF (?handler=Pdf), or email it to the customer (?handler=Email):
/// the PDF is saved on the estimate and sent as an attachment from the user's mailbox.
/// </summary>
public class ProposalModel(EstimateService estimates, OesDbContext db, ITenantContext tenant, MessagingService messaging, RecordItems items,
    ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ProposalEmailInput Mail { get; set; } = new();

    public EstimateVersion Version { get; private set; } = null!;
    public VersionContent Doc { get; private set; } = null!;
    public Estimate Estimate { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;
    /// <summary>The opportunity's contacts' email addresses, when the user may send email.</summary>
    public List<Recipient> Recipients { get; private set; } = [];
    public bool CanEmail { get; private set; }

    /// <summary>Set after issuing or signing: save this version's PDF on the estimate (once), then show the page.</summary>
    [BindProperty(SupportsGet = true)] public bool Save { get; set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync();
        var page = $"/Estimates/Proposal/{Id}";
        if (Save)
        {
            if (EstimateService.CurrentCopy(Version) is not null) return Redirect(page);
            var earlier = TempData.Peek("Flash") as string; // e.g. "Estimate issued…": keep it
            ViewData[PdfDocument.ViewDataKey] = true;
            return new PdfPageResult(FileName, Footer, page)
            {
                Deliver = async pdf =>
                {
                    await estimates.SaveVersionCopyAsync(Id, pdf);
                    return $"{earlier} A PDF copy is saved on the estimate.".Trim();
                },
            };
        }
        CanEmail = await user.HasPermissionAsync(Permissions.EmailSend);
        if (CanEmail)
        {
            Recipients = (await messaging.RecipientsAsync(EntityTypes.Opportunity, Estimate.OpportunityId))
                .Where(r => ConsentService.KindsFor(Channels.Email).Contains(r.Kind)).ToList();
            var me = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.FirstName).FirstOrDefaultAsync();
            Mail = new ProposalEmailInput
            {
                To = Recipients.Where(r => r.IsPrimary).Select(r => $"{r.ContactId}|{r.Address}").FirstOrDefault(),
                Subject = $"Your proposal from {Company.Name} ({Doc.Number})",
                Body = $"Hi {FirstName(Doc.CustomerName)},\n\nYour proposal for {Doc.Name} is attached. Let me know if you have any questions.\n\nThanks,\n{me}",
            };
        }
        return Page();
    });

    /// <summary>The same frozen version as a PDF download.</summary>
    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        await LoadAsync();
        return await PdfAsync(FileName, Footer, $"/Estimates/Proposal/{Id}");
    });

    /// <summary>
    /// For the field app, at api/v1/estimate-versions/{id}/proposal?handler=Copy (bearer tokens only work under /api):
    /// the saved PDF of the version as it stands now, made and saved first if there isn't one yet. Answers {documentId}.
    /// </summary>
    public Task<IActionResult> OnGetCopyAsync() => Guard(async () =>
    {
        await LoadAsync();
        if (EstimateService.CurrentCopy(Version) is { } saved) return new JsonResult(new { documentId = saved });
        ViewData[PdfDocument.ViewDataKey] = true;
        return new PdfPageResult(FileName, Footer, $"/Estimates/Proposal/{Id}")
        {
            Respond = async pdf => new JsonResult(new { documentId = await estimates.SaveVersionCopyAsync(Id, pdf) }),
        };
    });

    /// <summary>
    /// Emails the version's saved PDF to the chosen contact address. With no saved copy yet, renders and saves it first.
    /// </summary>
    public Task<IActionResult> OnPostEmailAsync() => Guard(async () =>
    {
        await LoadAsync();
        var page = $"/Estimates/Proposal/{Id}";
        var parts = (Mail.To ?? "").Split('|', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var contactId))
        {
            Flash("Pick who to send it to.", "danger");
            return Redirect(page);
        }
        async Task<string> SendAsync(Guid documentId)
        {
            var sent = await messaging.SendEmailAsync(new EmailInput
            {
                EntityType = EntityTypes.Opportunity, EntityId = Estimate.OpportunityId, ContactId = contactId, To = parts[1],
                Cc = Mail.Cc, Bcc = Mail.Bcc, Subject = Mail.Subject ?? "", Body = Mail.Body ?? "", DocumentIds = [documentId],
            });
            if (!sent.Ok) return $"The proposal is saved on the estimate, but the email didn't go through: {sent.Error}";
            await items.LogAsync(EntityTypes.Estimate, Estimate.Id, "estimate.emailed", $"Emailed proposal {Doc.Number} v{Doc.VersionNo} to {parts[1]}");
            await db.SaveChangesAsync();
            return $"Proposal emailed to {parts[1]}, with the PDF attached. A copy is saved on the estimate.";
        }
        if (EstimateService.CurrentCopy(Version) is { } saved)
        {
            var message = await SendAsync(saved);
            Flash(message, message.StartsWith("Proposal emailed") ? "success" : "danger");
            return Redirect(page);
        }
        ViewData[PdfDocument.ViewDataKey] = true;
        return new PdfPageResult(FileName, Footer, page) { Deliver = async pdf => await SendAsync(await estimates.SaveVersionCopyAsync(Id, pdf)) };
    });

    private string FileName => $"{Doc.Number}-v{Doc.VersionNo}";
    private string Footer => $"{Company.Name} · proposal {Doc.Number} v{Doc.VersionNo}";

    private async Task LoadAsync()
    {
        (Version, Doc, Estimate) = await estimates.GetVersionAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
    }

    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
}

public sealed class ProposalEmailInput
{
    /// <summary>"{contactId}|{address}" from the recipient list.</summary>
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
}
