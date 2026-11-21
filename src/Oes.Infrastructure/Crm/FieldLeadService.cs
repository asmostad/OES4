using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

/// <summary>A lead taken in the field: who, how to reach them, the address, and how it came in.</summary>
public sealed class FieldLeadInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? SourceKey { get; set; }
    public bool IsInsurance { get; set; }
    public string? Notes { get; set; }
    /// <summary>A property already on file (canvassing a storm), instead of typing the address.</summary>
    public Guid? PropertyId { get; set; }
    /// <summary>The storm being canvassed: ties the lead to it (and takes it off that storm's canvass list).</summary>
    public Guid? StormEventId { get; set; }
}

public sealed record FieldLeadResult(Guid OpportunityId, Guid ContactId, Guid? PropertyId);

/// <summary>
/// The field app's "New lead": the contact, the opportunity (owned by whoever took it) and the property, in one
/// transaction, so a failure part-way leaves nothing behind and the app's retry starts clean. An address that's
/// already on file is reused (when the user can see it), as on the web.
/// </summary>
public sealed class FieldLeadService(OesDbContext db, RecordScope scope, ICurrentUser user, ContactService contacts, OpportunityService opportunities)
{
    public async Task<FieldLeadResult> CreateAsync(FieldLeadInput i, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsCreate);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        if (string.IsNullOrWhiteSpace(i.FirstName) && string.IsNullOrWhiteSpace(i.LastName)) throw new CrmRuleException("Enter their name.");
        var hasAddress = i.PropertyId is null && !string.IsNullOrWhiteSpace(i.Line1);
        if (hasAddress && (string.IsNullOrWhiteSpace(i.City) || string.IsNullOrWhiteSpace(i.State) || string.IsNullOrWhiteSpace(i.PostalCode)))
            throw new CrmRuleException("Finish the address: city, state and ZIP.");

        var methods = new List<MethodInput>();
        if (!string.IsNullOrWhiteSpace(i.Phone)) methods.Add(new MethodInput(ContactMethodKinds.Mobile, i.Phone.Trim(), null, true));
        if (!string.IsNullOrWhiteSpace(i.Email)) methods.Add(new MethodInput(ContactMethodKinds.Email, i.Email.Trim(), null, true));
        var source = string.IsNullOrWhiteSpace(i.SourceKey) ? "door_knock" : i.SourceKey.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var contactId = await contacts.CreateAsync(new ContactInput
        {
            FirstName = i.FirstName?.Trim(), LastName = i.LastName?.Trim(), OwnerUserId = me, SourceKey = source, Methods = methods,
        }, ct);
        var oppId = await opportunities.CreateAsync(new OpportunityInput
        {
            PrimaryContactId = contactId, OwnerUserId = me, SourceKey = source, IsInsurance = i.IsInsurance || i.StormEventId is not null,
            Description = ContactService.Clean(i.Notes), StormEventId = i.StormEventId,
        }, ct);
        if (i.PropertyId is { } existing)
        {
            await opportunities.AttachPropertyAsync(oppId, existing, ct); // checks the user can see it, and links the contact
            await tx.CommitAsync(ct);
            return new FieldLeadResult(oppId, contactId, existing);
        }
        Guid? propertyId = hasAddress
            ? await opportunities.AttachNewPropertyAsync(oppId, new PropertyInput
            {
                Line1 = i.Line1!.Trim(), City = i.City!.Trim(), State = i.State!.Trim(), PostalCode = i.PostalCode!.Trim(),
            }, ct)
            : null;
        await tx.CommitAsync(ct);
        return new FieldLeadResult(oppId, contactId, propertyId);
    }
}
