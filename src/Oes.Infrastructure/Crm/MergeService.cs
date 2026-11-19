using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed record MergeCount(string Label, int Count);

/// <summary>What merging would move from the duplicate onto the record that's kept.</summary>
public sealed record MergePreview(Guid KeepId, string KeepName, Guid MergeId, string MergeName, List<MergeCount> Moves, List<string> Blockers);

/// <summary>
/// Merges duplicate contacts or properties. Everything that points at the duplicate is moved onto the kept record in
/// one transaction: opportunities, links, notes, documents, activity, messages, consents, tasks, appointments,
/// bookings, claims, measurements and so on. Blank fields on the kept record are filled from the duplicate; nothing
/// on the kept record is overwritten. Consent is conservative: if either record had opted out of a channel, the
/// kept record ends up opted out. The duplicate is archived and remembers where it went (MergedIntoId), so old
/// links still lead somewhere.
/// </summary>
public sealed class MergeService(OesDbContext db, RecordScope scope, ICurrentUser user, RecordItems items, IAuditLog audit)
{
    private async Task EnsureAllowedAsync(CancellationToken ct)
    {
        if (!await user.HasPermissionAsync(Permissions.RecordsMerge, ct)) throw new RecordForbiddenException("You don't have permission to merge records.");
    }

    // ---------------- contacts ----------------

    private async Task<(Contact Keep, Contact Merge)> ContactsAsync(Guid keepId, Guid mergeId, CancellationToken ct)
    {
        await EnsureAllowedAsync(ct);
        if (keepId == mergeId) throw new CrmRuleException("Pick two different contacts.");
        var visible = await scope.ContactsAsync();
        var keep = await visible.FirstOrDefaultAsync(c => c.Id == keepId, ct) ?? throw new RecordNotFoundException("Contact");
        var merge = await visible.FirstOrDefaultAsync(c => c.Id == mergeId, ct) ?? throw new RecordNotFoundException("Contact");
        return (keep, merge);
    }

    private async Task<List<string>> ContactBlockersAsync(Contact keep, Contact merge, CancellationToken ct)
    {
        var blockers = new List<string>();
        if (keep.Type != merge.Type) blockers.Add("A person can't be merged with an organization.");
        if (await db.Subcontractors.CountAsync(s => s.ContactId == keep.Id || s.ContactId == merge.Id, ct) > 1)
            blockers.Add("Both contacts have a subcontractor profile. Remove one first.");
        return blockers;
    }

    public async Task<MergePreview> PreviewContactsAsync(Guid keepId, Guid mergeId, CancellationToken ct = default)
    {
        var (keep, merge) = await ContactsAsync(keepId, mergeId, ct);
        var id = merge.Id;
        var moves = new List<MergeCount>
        {
            new("Opportunities", await db.Opportunities.CountAsync(o => o.PrimaryContactId == id, ct)),
            new("Properties", await db.PropertyContacts.CountAsync(p => p.ContactId == id, ct)),
            new("Phone numbers and emails", await db.ContactMethods.CountAsync(m => m.ContactId == id, ct)),
            new("Notes", await db.Notes.CountAsync(n => n.EntityType == EntityTypes.Contact && n.EntityId == id, ct)),
            new("Documents", await db.Documents.CountAsync(d => d.EntityType == EntityTypes.Contact && d.EntityId == id, ct)),
            new("Emails and texts", await db.EmailRecipients.CountAsync(r => r.ContactId == id, ct) + await db.SmsMessages.CountAsync(m => m.ContactId == id, ct)),
            new("Tasks and appointments", await db.Tasks.CountAsync(t => t.EntityType == EntityTypes.Contact && t.EntityId == id, ct)
                                          + await db.Appointments.CountAsync(a => a.EntityType == EntityTypes.Contact && a.EntityId == id, ct)),
            new("Bookings", await db.Bookings.CountAsync(b => b.ContactId == id, ct)),
            new("Claims (as adjuster)", await db.Claims.CountAsync(c => c.AdjusterContactId == id || c.DeskAdjusterContactId == id, ct)),
        };
        return new MergePreview(keep.Id, keep.DisplayName, merge.Id, merge.DisplayName, moves.Where(m => m.Count > 0).ToList(), await ContactBlockersAsync(keep, merge, ct));
    }

    public async Task MergeContactsAsync(Guid keepId, Guid mergeId, CancellationToken ct = default)
    {
        var (keep, merge) = await ContactsAsync(keepId, mergeId, ct);
        if ((await ContactBlockersAsync(keep, merge, ct)).FirstOrDefault() is { } blocker) throw new CrmRuleException(blocker);
        var (k, m) = (keep.Id, merge.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Fill blanks on the kept contact; never overwrite.
        keep.FirstName ??= merge.FirstName;
        keep.LastName ??= merge.LastName;
        keep.OrganizationName ??= merge.OrganizationName;
        keep.SourceKey ??= merge.SourceKey;
        keep.OwnerUserId ??= merge.OwnerUserId;
        keep.CautionNote = Join(keep.CautionNote, merge.CautionNote, 1000);
        keep.Notes = Join(keep.Notes, merge.Notes, 5000);
        if (Rank(merge.Status) > Rank(keep.Status)) keep.Status = merge.Status;
        keep.RefreshDisplayName();

        // Phone numbers and emails the kept contact doesn't have yet.
        var have = await db.ContactMethods.Where(x => x.ContactId == k).Select(x => x.Kind + "|" + x.NormalizedValue).ToListAsync(ct);
        foreach (var method in await db.ContactMethods.Where(x => x.ContactId == m).ToListAsync(ct))
        {
            if (have.Contains(method.Kind + "|" + method.NormalizedValue)) { db.ContactMethods.Remove(method); continue; }
            method.ContactId = k;
            method.IsPrimary = method.IsPrimary && !await db.ContactMethods.AnyAsync(x => x.ContactId == k && x.Kind == method.Kind && x.IsPrimary, ct);
        }

        // Organization relationships: re-point, dropping self-links and duplicates.
        foreach (var r in await db.ContactRelationships.Where(x => x.PersonContactId == m || x.OrganizationContactId == m).ToListAsync(ct))
        {
            if (r.PersonContactId == m) r.PersonContactId = k;
            if (r.OrganizationContactId == m) r.OrganizationContactId = k;
            if (r.PersonContactId == r.OrganizationContactId
                || await db.ContactRelationships.AnyAsync(x => x.Id != r.Id && x.PersonContactId == r.PersonContactId && x.OrganizationContactId == r.OrganizationContactId && x.EndedOn == null, ct))
                db.ContactRelationships.Remove(r);
        }

        // Property links: drop a current link the kept contact already has (same property and role).
        foreach (var pc in await db.PropertyContacts.Where(x => x.ContactId == m).ToListAsync(ct))
        {
            if (pc.EndedOn is null && await db.PropertyContacts.AnyAsync(x => x.ContactId == k && x.PropertyId == pc.PropertyId && x.RoleKey == pc.RoleKey && x.EndedOn == null, ct))
                db.PropertyContacts.Remove(pc);
            else pc.ContactId = k;
        }

        // Consent: opting out wins. Work out each record's current status per channel and purpose before moving the history.
        var consents = await db.ContactConsents.Where(x => x.ContactId == k || x.ContactId == m).ToListAsync(ct);
        var optedOut = consents.GroupBy(x => (x.ContactId, x.Channel, x.Purpose))
            .Select(g => g.OrderByDescending(x => x.At).ThenByDescending(x => x.CreatedAt).First())
            .Where(x => x.Status == ConsentStatuses.OptedOut).Select(x => (x.Channel, x.Purpose)).Distinct().ToList();
        await db.SaveChangesAsync(ct);

        await db.ContactConsents.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);
        var now = DateTime.UtcNow;
        foreach (var (channel, purpose) in optedOut)
        {
            var current = await db.ContactConsents.Where(x => x.ContactId == k && x.Channel == channel && x.Purpose == purpose)
                .OrderByDescending(x => x.At).ThenByDescending(x => x.CreatedAt).Select(x => x.Status).FirstAsync(ct);
            if (current != ConsentStatuses.OptedOut)
                db.ContactConsents.Add(new ContactConsent { ContactId = k, Channel = channel, Purpose = purpose, Status = ConsentStatuses.OptedOut, Source = "merge",
                    Evidence = $"Kept from {merge.DisplayName} when the contacts were merged", RecordedByUserId = user.UserId, At = now });
        }

        await db.Opportunities.Where(x => x.PrimaryContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PrimaryContactId, k), ct);
        await db.Bookings.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);
        await db.EmailRecipients.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);
        await db.SmsMessages.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);
        await db.Subcontractors.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);
        await db.Claims.Where(x => x.AdjusterContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.AdjusterContactId, k), ct);
        await db.Claims.Where(x => x.DeskAdjusterContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeskAdjusterContactId, k), ct);

        // Blasts: one recipient row per contact per blast.
        var keptBlasts = await db.BlastRecipients.Where(x => x.ContactId == k).Select(x => x.BlastId).ToListAsync(ct);
        await db.BlastRecipients.Where(x => x.ContactId == m && keptBlasts.Contains(x.BlastId)).ExecuteDeleteAsync(ct);
        await db.BlastRecipients.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);

        // Nurture: if both are active in the same sequence, stop the duplicate's run.
        var keptActive = await db.Enrollments.Where(x => x.ContactId == k && x.Status == EnrollmentStatuses.Active).Select(x => x.SequenceId).ToListAsync(ct);
        await db.Enrollments.Where(x => x.ContactId == m && x.Status == EnrollmentStatuses.Active && keptActive.Contains(x.SequenceId))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, EnrollmentStatuses.Stopped).SetProperty(x => x.StopReason, "merged")
                .SetProperty(x => x.NextRunAt, (DateTime?)null).SetProperty(x => x.FinishedAt, now), ct);
        await db.Enrollments.Where(x => x.ContactId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactId, k), ct);

        await MoveRecordItemsAsync(EntityTypes.Contact, m, k, ct);

        merge.MergedIntoId = k;
        merge.ArchivedAt = now;
        await items.LogAsync(EntityTypes.Contact, k, "contact.merged", $"Merged {merge.DisplayName} into this contact", ct);
        audit.Record("contact.merged", "Contact", k, $"Merged {merge.DisplayName} into {keep.DisplayName}", new { mergedId = m });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static int Rank(string status) => status switch { ContactStatuses.Customer => 2, ContactStatuses.PastCustomer => 1, _ => 0 };

    private static string? Join(string? a, string? b, int max)
    {
        if (string.IsNullOrWhiteSpace(b) || a?.Contains(b.Trim(), StringComparison.Ordinal) == true) return a;
        var joined = string.IsNullOrWhiteSpace(a) ? b.Trim() : a.TrimEnd() + "\n\n" + b.Trim();
        return joined.Length <= max ? joined : joined[..max];
    }

    // ---------------- properties ----------------

    private async Task<(Property Keep, Property Merge)> PropertiesAsync(Guid keepId, Guid mergeId, CancellationToken ct)
    {
        await EnsureAllowedAsync(ct);
        if (keepId == mergeId) throw new CrmRuleException("Pick two different properties.");
        var visible = await scope.PropertiesAsync();
        var keep = await visible.FirstOrDefaultAsync(p => p.Id == keepId, ct) ?? throw new RecordNotFoundException("Property");
        var merge = await visible.FirstOrDefaultAsync(p => p.Id == mergeId, ct) ?? throw new RecordNotFoundException("Property");
        return (keep, merge);
    }

    public async Task<MergePreview> PreviewPropertiesAsync(Guid keepId, Guid mergeId, CancellationToken ct = default)
    {
        var (keep, merge) = await PropertiesAsync(keepId, mergeId, ct);
        var id = merge.Id;
        var moves = new List<MergeCount>
        {
            new("Buildings (with their measurements)", await db.PropertyStructures.CountAsync(s => s.PropertyId == id, ct)),
            new("Contacts", await db.PropertyContacts.CountAsync(p => p.PropertyId == id, ct)),
            new("Opportunities", await db.Opportunities.CountAsync(o => o.PropertyId == id, ct)),
            new("Estimates", await db.Estimates.CountAsync(e => e.PropertyId == id, ct)),
            new("Claims", await db.Claims.CountAsync(c => c.PropertyId == id, ct)),
            new("Projects", await db.Projects.CountAsync(p => p.PropertyId == id, ct)),
            new("Notes", await db.Notes.CountAsync(n => n.EntityType == EntityTypes.Property && n.EntityId == id, ct)),
            new("Documents and photos", await db.Documents.CountAsync(d => d.EntityType == EntityTypes.Property && d.EntityId == id, ct)),
        };
        return new MergePreview(keep.Id, keep.SingleLine, merge.Id, merge.SingleLine, moves.Where(m => m.Count > 0).ToList(), []);
    }

    public async Task MergePropertiesAsync(Guid keepId, Guid mergeId, CancellationToken ct = default)
    {
        var (keep, merge) = await PropertiesAsync(keepId, mergeId, ct);
        var (k, m) = (keep.Id, merge.Id);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        keep.Line2 ??= merge.Line2;
        keep.Latitude ??= merge.Latitude;
        keep.Longitude ??= merge.Longitude;
        keep.Notes = Join(keep.Notes, merge.Notes, 5000);

        // Buildings move with their measurements; a name the kept property already uses gets a suffix.
        var names = await db.PropertyStructures.Where(s => s.PropertyId == k).Select(s => s.Name).ToListAsync(ct);
        var sort = (await db.PropertyStructures.Where(s => s.PropertyId == k).MaxAsync(s => (int?)s.Sort, ct) ?? 0) + 10;
        foreach (var s in await db.PropertyStructures.Where(s => s.PropertyId == m).OrderBy(s => s.Sort).ToListAsync(ct))
        {
            s.PropertyId = k;
            s.Sort = sort;
            sort += 10;
            if (names.Contains(s.Name))
            {
                var name = $"{s.Name} (from {merge.Line1})";
                s.Name = name.Length <= 100 ? name : name[..100];
            }
            names.Add(s.Name);
        }

        foreach (var pc in await db.PropertyContacts.Where(x => x.PropertyId == m).ToListAsync(ct))
        {
            if (pc.EndedOn is null && await db.PropertyContacts.AnyAsync(x => x.PropertyId == k && x.ContactId == pc.ContactId && x.RoleKey == pc.RoleKey && x.EndedOn == null, ct))
                db.PropertyContacts.Remove(pc);
            else
            {
                pc.PropertyId = k;
                pc.IsPrimary = pc.IsPrimary && !await db.PropertyContacts.AnyAsync(x => x.PropertyId == k && x.IsPrimary && x.EndedOn == null, ct);
            }
        }
        await db.SaveChangesAsync(ct);

        await db.Opportunities.Where(x => x.PropertyId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PropertyId, k), ct);
        await db.Estimates.Where(x => x.PropertyId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PropertyId, k), ct);
        await db.Claims.Where(x => x.PropertyId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PropertyId, k), ct);
        await db.Projects.Where(x => x.PropertyId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PropertyId, k), ct);
        await db.MeasurementSets.Where(x => x.PropertyId == m).ExecuteUpdateAsync(u => u.SetProperty(x => x.PropertyId, k), ct);
        await MoveRecordItemsAsync(EntityTypes.Property, m, k, ct);

        merge.MergedIntoId = k;
        merge.ArchivedAt = DateTime.UtcNow;
        await items.LogAsync(EntityTypes.Property, k, "property.merged", $"Merged {merge.SingleLine} into this property", ct);
        audit.Record("property.merged", "Property", k, $"Merged {merge.SingleLine} into {keep.SingleLine}", new { mergedId = m });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------------- likely duplicates ----------------

    /// <summary>Contacts sharing a phone number or email with this one, or with the same name.</summary>
    public async Task<List<(Guid Id, string Name, string Why)>> SuggestContactsAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureAllowedAsync(ct);
        var visible = await scope.ContactsAsync();
        var c = await visible.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Contact");
        var values = await db.ContactMethods.Where(x => x.ContactId == id).Select(x => x.NormalizedValue).ToListAsync(ct);
        var byMethod = await (from o in visible where o.Id != id && o.Type == c.Type
                              join x in db.ContactMethods on o.Id equals x.ContactId
                              where values.Contains(x.NormalizedValue)
                              select new { o.Id, o.DisplayName, Value = x.NormalizedValue }).Take(20).ToListAsync(ct);
        var byName = await visible.Where(o => o.Id != id && o.Type == c.Type && o.DisplayName == c.DisplayName)
            .Select(o => new { o.Id, o.DisplayName }).Take(10).ToListAsync(ct);
        return byMethod.Select(x => (x.Id, x.DisplayName, $"Same {x.Value}"))
            .Concat(byName.Select(x => (x.Id, x.DisplayName, "Same name")))
            .GroupBy(x => x.Id).Select(g => (g.Key, g.First().Item2, string.Join(", ", g.Select(x => x.Item3).Distinct()))).ToList();
    }

    /// <summary>Properties at the same or a nearly identical address.</summary>
    public async Task<List<(Guid Id, string Name, string Why)>> SuggestPropertiesAsync(Guid id, PropertyService properties, CancellationToken ct = default)
    {
        await EnsureAllowedAsync(ct);
        var p = await (await scope.PropertiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Property");
        var matches = await properties.FindMatchesAsync(new PropertyInput { Line1 = p.Line1, Line2 = p.Line2, City = p.City, State = p.State, PostalCode = p.PostalCode }, ct);
        return matches.Where(x => x.Id != id).Select(x => (x.Id, x.Address, x.Exact ? "Same address" : "Similar address")).ToList();
    }

    // ---------------- shared ----------------

    /// <summary>Notes, documents, activity, rollup links, tasks, appointments, messages, AI chats and customer links.</summary>
    private async Task MoveRecordItemsAsync(string type, Guid from, Guid to, CancellationToken ct)
    {
        await db.Notes.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.Documents.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.ActivityEvents.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.Tasks.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.Appointments.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.EmailMessages.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.SmsMessages.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.AiConversations.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
        await db.ExternalAccessTokens.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);

        // Rollup links are unique per (item, record): drop the ones the kept record already has.
        var kept = (await db.RecordLinks.Where(x => x.EntityType == type && x.EntityId == to).Select(x => new { x.ItemType, x.ItemId }).ToListAsync(ct))
            .Select(x => x.ItemType + "|" + x.ItemId).ToHashSet();
        var moving = await db.RecordLinks.Where(x => x.EntityType == type && x.EntityId == from).Select(x => new { x.Id, x.ItemType, x.ItemId }).ToListAsync(ct);
        var duplicates = moving.Where(x => kept.Contains(x.ItemType + "|" + x.ItemId)).Select(x => x.Id).ToList();
        if (duplicates.Count > 0) await db.RecordLinks.Where(x => duplicates.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.RecordLinks.Where(x => x.EntityType == type && x.EntityId == from).ExecuteUpdateAsync(u => u.SetProperty(x => x.EntityId, to), ct);
    }
}
