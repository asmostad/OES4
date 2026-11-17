using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed class RecordNotFoundException(string what) : Exception($"{what} was not found or you don't have access to it.");

public sealed class RecordForbiddenException(string message) : Exception(message);

public sealed class CrmRuleException(string message) : Exception(message);

/// <summary>
/// Record-level visibility (architecture §4.2). Without records.view_all a user sees records
/// they own plus the unassigned pool; properties follow the contacts and opportunities on
/// them. Used by pages, API, search, file downloads and (later) AI tools, so every path
/// applies the same rule. OES3 had no record-level checks at all.
/// </summary>
public sealed class RecordScope(OesDbContext db, ICurrentUser user)
{
    private bool? _viewAll;

    public Guid? Me => user.UserId;

    public async Task<bool> ViewAllAsync() => _viewAll ??= await user.HasPermissionAsync(Permissions.RecordsViewAll);

    public async Task<IQueryable<Contact>> ContactsAsync(bool includeArchived = false)
    {
        var q = db.Contacts.AsQueryable();
        if (!includeArchived) q = q.Where(c => c.ArchivedAt == null);
        if (await ViewAllAsync()) return q;
        var me = Me;
        // Own or unassigned, or linked to an opportunity the user owns.
        return q.Where(c => c.OwnerUserId == null || c.OwnerUserId == me
                            || db.Opportunities.Any(o => o.PrimaryContactId == c.Id && o.OwnerUserId == me));
    }

    public async Task<IQueryable<Opportunity>> OpportunitiesAsync(bool includeArchived = false)
    {
        var q = db.Opportunities.AsQueryable();
        if (!includeArchived) q = q.Where(o => o.ArchivedAt == null);
        if (await ViewAllAsync()) return q;
        var me = Me;
        return q.Where(o => o.OwnerUserId == null || o.OwnerUserId == me);
    }

    public async Task<IQueryable<Property>> PropertiesAsync(bool includeArchived = false)
    {
        var q = db.Properties.AsQueryable();
        if (!includeArchived) q = q.Where(p => p.ArchivedAt == null);
        if (await ViewAllAsync()) return q;
        var me = Me;
        var contacts = await ContactsAsync(includeArchived: true);
        var opps = await OpportunitiesAsync(includeArchived: true);
        return q.Where(p => p.CreatedByUserId == me
                            || opps.Any(o => o.PropertyId == p.Id)
                            || db.PropertyContacts.Any(pc => pc.PropertyId == p.Id && contacts.Any(c => c.Id == pc.ContactId)));
    }

    /// <summary>Claims follow their opportunity's visibility.</summary>
    public async Task<IQueryable<Domain.Insurance.InsuranceClaim>> ClaimsAsync(bool includeArchived = false)
    {
        var q = db.Claims.AsQueryable();
        if (!includeArchived) q = q.Where(c => c.ArchivedAt == null);
        if (await ViewAllAsync()) return q;
        var opps = await OpportunitiesAsync(includeArchived: true);
        return q.Where(c => opps.Any(o => o.Id == c.OpportunityId));
    }

    public async Task<bool> CanSeeAsync(string entityType, Guid id) => entityType switch
    {
        EntityTypes.Contact => await (await ContactsAsync(true)).AnyAsync(c => c.Id == id),
        EntityTypes.Property => await (await PropertiesAsync(true)).AnyAsync(p => p.Id == id),
        EntityTypes.Opportunity => await (await OpportunitiesAsync(true)).AnyAsync(o => o.Id == id),
        EntityTypes.Claim => await (await ClaimsAsync(true)).AnyAsync(c => c.Id == id),
        _ => false,
    };

    public async Task EnsureCanSeeAsync(string entityType, Guid id)
    {
        if (!await CanSeeAsync(entityType, id)) throw new RecordNotFoundException(Describe(entityType));
    }

    /// <summary>records.edit_all, or records.edit_own for records the user owns or that are unassigned.</summary>
    public async Task<bool> CanEditAsync(Guid? ownerUserId)
    {
        if (await user.HasPermissionAsync(Permissions.RecordsEditAll)) return true;
        return await user.HasPermissionAsync(Permissions.RecordsEditOwn) && (ownerUserId is null || ownerUserId == Me);
    }

    public async Task EnsureCanEditAsync(Guid? ownerUserId)
    {
        if (!await CanEditAsync(ownerUserId)) throw new RecordForbiddenException("You don't have permission to change this record.");
    }

    public async Task EnsurePermissionAsync(string permission)
    {
        if (!await user.HasPermissionAsync(permission)) throw new RecordForbiddenException("You don't have permission to do that.");
    }

    public static string Describe(string entityType) => entityType switch
    {
        EntityTypes.Contact => "Contact",
        EntityTypes.Property => "Property",
        EntityTypes.Opportunity => "Opportunity",
        EntityTypes.Claim => "Claim",
        _ => "Record",
    };
}
