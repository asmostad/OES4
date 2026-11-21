using Oes.Infrastructure.Communication;

namespace Oes.Web.Infrastructure;

/// <summary>What the "email this document" panel on a print view posts (fields named Mail.*).</summary>
public sealed class DocumentEmailInput
{
    /// <summary>"{contactId}|{address}" when picking a contact, or a typed address (suppliers).</summary>
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    /// <summary>Order sheets: also mark the order placed, with this supplier name.</summary>
    public bool MarkOrdered { get; set; }
    public string? Supplier { get; set; }

    /// <summary>The contact and address from a "{contactId}|{address}" pick, or null.</summary>
    public (Guid ContactId, string Address)? ContactPick() =>
        (To ?? "").Split('|', 2) is [var id, var address] && Guid.TryParse(id, out var contactId) ? (contactId, address) : null;
}

/// <summary>The panel's settings: who it can go to, what's prefilled, the file it attaches.</summary>
public sealed class DocumentEmailPanel
{
    public required string Title { get; init; }
    public required string Handler { get; init; }
    public required string AttachmentName { get; init; }
    /// <summary>Contacts to pick from; null means a typed address.</summary>
    public List<Recipient>? Recipients { get; init; }
    public required DocumentEmailInput Mail { get; init; }
    public string? NoRecipients { get; init; }
    /// <summary>Order sheets: offer "mark as ordered".</summary>
    public bool OfferMarkOrdered { get; init; }
}
