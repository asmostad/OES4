using Oes.Domain.Common;

namespace Oes.Domain.Crm;

public static class ContactTypes
{
    public const string Person = "person";
    public const string Organization = "organization";
}

public static class ContactStatuses
{
    public const string Lead = "lead";
    public const string Customer = "customer";
    public const string PastCustomer = "past_customer";
}

/// <summary>
/// One table for people and organizations (replaces OES3 consumers, companies and
/// company_contacts). People can belong to organizations through ContactRelationship.
/// </summary>
public class Contact : TenantEntity, IArchivable
{
    public string Type { get; set; } = ContactTypes.Person;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? OrganizationName { get; set; }

    /// <summary>Stored for sorting and search: "First Last" or the organization name.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Lead / customer / past customer, derived from the contact's opportunities.</summary>
    public string Status { get; set; } = ContactStatuses.Lead;

    public Guid? OwnerUserId { get; set; }
    public string? SourceKey { get; set; }
    public string? CautionNote { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public DateTime? ArchivedAt { get; set; }
    /// <summary>Set when this contact was merged into another; the other one holds everything now.</summary>
    public Guid? MergedIntoId { get; set; }

    public List<ContactMethod> Methods { get; set; } = [];

    public bool IsOrganization => Type == ContactTypes.Organization;

    public void RefreshDisplayName() =>
        DisplayName = IsOrganization
            ? (OrganizationName ?? "").Trim()
            : $"{FirstName} {LastName}".Trim();
}

public static class ContactMethodKinds
{
    public const string Email = "email";
    public const string Mobile = "mobile";
    public const string Phone = "phone";
    public const string Fax = "fax";
    public const string Website = "website";

    public static readonly string[] All = [Email, Mobile, Phone, Fax, Website];
    public static bool IsPhone(string kind) => kind is Mobile or Phone or Fax;
}

/// <summary>Many emails and phones per contact. NormalizedValue is indexed for matching inbound mail/SMS.</summary>
public class ContactMethod : TenantEntity
{
    public Guid ContactId { get; set; }
    public string Kind { get; set; } = ContactMethodKinds.Email;
    public string Value { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Person ↔ organization. One person can work for several organizations.</summary>
public class ContactRelationship : TenantEntity
{
    public Guid PersonContactId { get; set; }
    public Guid OrganizationContactId { get; set; }
    public string? Title { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly? EndedOn { get; set; }
}

public interface IArchivable
{
    DateTime? ArchivedAt { get; set; }
}
