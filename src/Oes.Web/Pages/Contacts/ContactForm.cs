using System.ComponentModel.DataAnnotations;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;

namespace Oes.Web.Pages.Contacts;

public sealed class MethodRow
{
    public string Kind { get; set; } = ContactMethodKinds.Email;
    [StringLength(254)] public string? Value { get; set; }
    [StringLength(50)] public string? Label { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Form fields shared by New and Edit.</summary>
public sealed class ContactForm
{
    public string Type { get; set; } = ContactTypes.Person;
    [StringLength(100), Display(Name = "First name")] public string? FirstName { get; set; }
    [StringLength(100), Display(Name = "Last name")] public string? LastName { get; set; }
    [StringLength(200), Display(Name = "Organization name")] public string? OrganizationName { get; set; }
    [Display(Name = "Owner")] public Guid? OwnerUserId { get; set; }
    [Display(Name = "Lead source")] public string? SourceKey { get; set; }
    [StringLength(500), Display(Name = "Caution note")] public string? CautionNote { get; set; }
    [StringLength(5000)] public string? Notes { get; set; }
    public List<MethodRow> Methods { get; set; } = [];

    // New only
    public Guid? OrganizationId { get; set; }
    public string? OrganizationLabel { get; set; }
    [StringLength(100)] public string? Title { get; set; }

    public bool IsOrganization => Type == ContactTypes.Organization;

    public ContactInput ToInput() => new()
    {
        Type = Type, FirstName = FirstName, LastName = LastName, OrganizationName = OrganizationName, OwnerUserId = OwnerUserId,
        SourceKey = SourceKey, CautionNote = CautionNote, Notes = Notes, OrganizationId = OrganizationId, Title = Title,
        Methods = Methods.Where(m => !string.IsNullOrWhiteSpace(m.Value)).Select(m => new MethodInput(m.Kind, m.Value!, m.Label, m.IsPrimary)).ToList(),
    };

    /// <summary>Makes sure the form shows at least one email and one phone row, plus a spare.</summary>
    public void PadMethods()
    {
        if (!Methods.Any(m => m.Kind == ContactMethodKinds.Email)) Methods.Add(new MethodRow { Kind = ContactMethodKinds.Email });
        if (!Methods.Any(m => ContactMethodKinds.IsPhone(m.Kind))) Methods.Add(new MethodRow { Kind = ContactMethodKinds.Mobile });
        Methods.Add(new MethodRow { Kind = ContactMethodKinds.Phone });
    }

    public static ContactForm From(Contact c) => new()
    {
        Type = c.Type, FirstName = c.FirstName, LastName = c.LastName, OrganizationName = c.OrganizationName, OwnerUserId = c.OwnerUserId,
        SourceKey = c.SourceKey, CautionNote = c.CautionNote, Notes = c.Notes,
        Methods = c.Methods.Select(m => new MethodRow { Kind = m.Kind, Value = m.Value, Label = m.Label, IsPrimary = m.IsPrimary }).ToList(),
    };
}
