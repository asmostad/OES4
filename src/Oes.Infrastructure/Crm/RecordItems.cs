using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed record TimelineItem(
    string ItemType, Guid Id, DateTime At, string? AuthorName, string Text, bool IsPinned,
    string EntityType, Guid EntityId, string? FileName, long? SizeBytes, string? CategoryKey, bool CanEdit)
{
    /// <summary>Set for email and text items.</summary>
    public TimelineMessage? Message { get; init; }
}

public sealed record TimelineMessage(string Direction, string Status, string? Subject, string Counterparty)
{
    /// <summary>Files an email carried (stored documents).</summary>
    public IReadOnlyList<TimelineAttachment> Attachments { get; init; } = [];
}

public sealed record TimelineAttachment(Guid DocumentId, string FileName);

public enum TimelineScope { ThisRecord, Everything }

/// <summary>
/// Notes, documents and activity on any record, plus the record_links rollup that makes
/// "everything under this record" a single indexed lookup.
/// </summary>
public sealed class RecordItems(OesDbContext db, RecordScope scope, IFileStore files, ICurrentUser user, IClock clock)
{
    /// <summary>The record plus every record above it, which an item written on it is also linked to.</summary>
    public async Task<List<(string Type, Guid Id)>> LinkTargetsAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var targets = new List<(string, Guid)> { (entityType, entityId) };
        if (entityType == EntityTypes.WorkOrder)
        {
            var wo = db.WorkOrders.Local.FirstOrDefault(x => x.Id == entityId) ?? await db.WorkOrders.FirstAsync(x => x.Id == entityId, ct);
            targets.AddRange(await LinkTargetsAsync(EntityTypes.Trade, wo.ProjectTradeId, ct));
            return targets;
        }
        if (entityType == EntityTypes.Trade)
        {
            var trade = db.ProjectTrades.Local.FirstOrDefault(x => x.Id == entityId) ?? await db.ProjectTrades.FirstAsync(x => x.Id == entityId, ct);
            targets.AddRange(await LinkTargetsAsync(EntityTypes.Project, trade.ProjectId, ct));
            return targets;
        }
        if (entityType == EntityTypes.Project)
        {
            var project = db.Projects.Local.FirstOrDefault(x => x.Id == entityId) ?? await db.Projects.FirstAsync(x => x.Id == entityId, ct);
            targets.AddRange(await LinkTargetsAsync(EntityTypes.Opportunity, project.OpportunityId, ct));
            return targets;
        }
        if (entityType == EntityTypes.Estimate)
        {
            var est = db.Estimates.Local.FirstOrDefault(x => x.Id == entityId) ?? await db.Estimates.FirstAsync(x => x.Id == entityId, ct);
            targets.AddRange(await LinkTargetsAsync(EntityTypes.Opportunity, est.OpportunityId, ct));
            return targets;
        }
        if (entityType == EntityTypes.Claim)
        {
            // A claim sits under its opportunity: link to the claim, then everything above the opportunity.
            var claim = db.Claims.Local.FirstOrDefault(x => x.Id == entityId) ?? await db.Claims.FirstAsync(x => x.Id == entityId, ct);
            targets.AddRange(await LinkTargetsAsync(EntityTypes.Opportunity, claim.OpportunityId, ct));
            return targets;
        }
        if (entityType == EntityTypes.Opportunity)
        {
            // Prefer the tracked entity: the opportunity may have been created in this unit of work.
            var o = db.Opportunities.Local.FirstOrDefault(x => x.Id == entityId)
                    ?? await db.Opportunities.FirstAsync(x => x.Id == entityId, ct);
            targets.Add((EntityTypes.Contact, o.PrimaryContactId));
            if (o.PropertyId is { } p) targets.Add((EntityTypes.Property, p));
        }
        return targets;
    }

    /// <summary>Links an item (task, appointment…) to a record and everything above it. Saved with the caller's SaveChanges.</summary>
    public Task LinkItemAsync(string itemType, Guid itemId, string entityType, Guid entityId, CancellationToken ct = default) =>
        LinkAsync(itemType, itemId, entityType, entityId, ct);

    private async Task LinkAsync(string itemType, Guid itemId, string entityType, Guid entityId, CancellationToken ct)
    {
        foreach (var (type, id) in await LinkTargetsAsync(entityType, entityId, ct))
            db.RecordLinks.Add(new RecordLink
            {
                ItemType = itemType, ItemId = itemId, EntityType = type, EntityId = id,
                IsPrimary = type == entityType && id == entityId,
            });
    }

    /// <summary>
    /// Adds links from every item on <paramref name="fromType"/>/<paramref name="fromId"/> to a
    /// newly attached parent (e.g. an opportunity's items once it gets a property).
    /// </summary>
    public async Task PropagateLinksAsync(string fromType, Guid fromId, string toType, Guid toId, CancellationToken ct = default)
    {
        var items = await db.RecordLinks.Where(l => l.EntityType == fromType && l.EntityId == fromId)
            .Select(l => new { l.ItemType, l.ItemId }).ToListAsync(ct);
        var already = await db.RecordLinks.Where(l => l.EntityType == toType && l.EntityId == toId)
            .Select(l => l.ItemId).ToListAsync(ct);
        foreach (var i in items.Where(i => !already.Contains(i.ItemId)))
            db.RecordLinks.Add(new RecordLink { ItemType = i.ItemType, ItemId = i.ItemId, EntityType = toType, EntityId = toId });
    }

    /// <summary>Records an activity event; saved with the caller's SaveChanges.</summary>
    public async Task LogAsync(string entityType, Guid entityId, string eventType, string summary, CancellationToken ct = default)
    {
        var a = new ActivityEvent
        {
            EntityType = entityType, EntityId = entityId, EventType = eventType, Summary = summary, ActorUserId = user.UserId,
        };
        db.ActivityEvents.Add(a);
        await LinkAsync(ItemTypes.Activity, a.Id, entityType, entityId, ct);
    }

    public async Task<Guid> AddNoteAsync(string entityType, Guid entityId, string body, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(entityType, entityId);
        body = body.Trim();
        if (body.Length == 0) throw new CrmRuleException("The note is empty.");
        if (body.Length > 20000) throw new CrmRuleException("The note is too long.");

        var note = new Note { Body = body, AuthorUserId = user.UserId, EntityType = entityType, EntityId = entityId };
        db.Notes.Add(note);
        await LinkAsync(ItemTypes.Note, note.Id, entityType, entityId, ct);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task UpdateNoteAsync(Guid noteId, string body, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new RecordNotFoundException("Note");
        await scope.EnsureCanSeeAsync(note.EntityType, note.EntityId);
        if (note.AuthorUserId != user.UserId) throw new RecordForbiddenException("Only the author can edit a note.");
        note.Body = body.Trim();
        note.EditedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task TogglePinAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new RecordNotFoundException("Note");
        await scope.EnsureCanSeeAsync(note.EntityType, note.EntityId);
        note.IsPinned = !note.IsPinned;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new RecordNotFoundException("Note");
        await scope.EnsureCanSeeAsync(note.EntityType, note.EntityId);
        if (note.AuthorUserId != user.UserId) throw new RecordForbiddenException("Only the author can delete a note.");
        db.RecordLinks.RemoveRange(db.RecordLinks.Where(l => l.ItemType == ItemTypes.Note && l.ItemId == noteId));
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> UploadAsync(string entityType, Guid entityId, Stream content, string fileName, string contentType,
        string? categoryKey, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(entityType, entityId);
        return await StoreAsync(entityType, entityId, content, fileName, contentType, categoryKey, "upload", ct);
    }

    /// <summary>
    /// For callers already authorized another way (a sub portal link): no user record-scope check.
    /// The tenant context must already be set from the verified token.
    /// </summary>
    public Task<Guid> UploadFromLinkAsync(string entityType, Guid entityId, Stream content, string fileName, string contentType,
        string? categoryKey, CancellationToken ct = default) =>
        StoreAsync(entityType, entityId, content, fileName, contentType, categoryKey, "portal", ct);

    private async Task<Guid> StoreAsync(string entityType, Guid entityId, Stream content, string fileName, string contentType,
        string? categoryKey, string source, CancellationToken ct)
    {

        // Hash while storing, for duplicate detection later.
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var hashing = new HashingStream(content, sha);
        var stored = await files.SaveAsync(hashing, fileName, contentType, ct);

        var doc = new Document
        {
            StorageKey = stored.Key, FileName = Path.GetFileName(fileName), ContentType = contentType,
            SizeBytes = stored.Length, Sha256 = Convert.ToHexStringLower(sha.GetHashAndReset()),
            CategoryKey = categoryKey, UploadedByUserId = user.UserId, EntityType = entityType, EntityId = entityId, Source = source,
        };
        db.Documents.Add(doc);
        await LinkAsync(ItemTypes.Document, doc.Id, entityType, entityId, ct);
        await LogAsync(entityType, entityId, "document.uploaded", $"Uploaded {doc.FileName}", ct);
        await db.SaveChangesAsync(ct);
        return doc.Id;
    }

    /// <summary>Opens a document only if the user can see the record it belongs to.</summary>
    public async Task<(Document Doc, Stream Content)> OpenDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.ArchivedAt == null, ct)
                  ?? throw new RecordNotFoundException("Document");
        await scope.EnsureCanSeeAsync(doc.EntityType, doc.EntityId);
        return (doc, await files.OpenReadAsync(doc.StorageKey, ct));
    }

    public async Task ArchiveDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.ArchivedAt == null, ct)
                  ?? throw new RecordNotFoundException("Document");
        await scope.EnsureCanSeeAsync(doc.EntityType, doc.EntityId);
        doc.ArchivedAt = clock.UtcNow;
        await LogAsync(doc.EntityType, doc.EntityId, "document.archived", $"Removed {doc.FileName}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A stored file that can be attached to an email from a record.</summary>
    public sealed record AttachableDocument(Guid Id, string FileName, long SizeBytes, string? CategoryKey, DateTime CreatedAt, string ContentType = "");

    /// <summary>
    /// Files on this record and the records under it (an opportunity's estimates, say), newest first, that this user
    /// can open: the files the email form offers to attach.
    /// </summary>
    public async Task<List<AttachableDocument>> AttachableDocumentsAsync(string entityType, Guid entityId, int take = 30, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(entityType, entityId);
        var rows = await (from l in db.RecordLinks
                          where l.EntityType == entityType && l.EntityId == entityId && l.ItemType == ItemTypes.Document
                          join d in db.Documents on l.ItemId equals d.Id
                          where d.ArchivedAt == null
                          orderby d.CreatedAt descending
                          select new { d.Id, d.FileName, d.SizeBytes, d.CategoryKey, d.CreatedAt, d.EntityType, d.EntityId, d.ContentType }).Take(take * 2).ToListAsync(ct);
        var visible = new Dictionary<(string, Guid), bool>();
        var list = new List<AttachableDocument>();
        foreach (var r in rows.DistinctBy(r => r.Id))
        {
            if (!visible.TryGetValue((r.EntityType, r.EntityId), out var ok)) visible[(r.EntityType, r.EntityId)] = ok = await scope.CanSeeAsync(r.EntityType, r.EntityId);
            if (ok) list.Add(new AttachableDocument(r.Id, r.FileName, r.SizeBytes, r.CategoryKey, r.CreatedAt, r.ContentType));
            if (list.Count == take) break;
        }
        return list;
    }

    public async Task<List<TimelineItem>> TimelineAsync(string entityType, Guid entityId, TimelineScope range, string? itemType = null,
        int take = 200, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(entityType, entityId);

        var links = db.RecordLinks.Where(l => l.EntityType == entityType && l.EntityId == entityId);
        if (range == TimelineScope.ThisRecord) links = links.Where(l => l.IsPrimary);
        if (itemType is not null) links = links.Where(l => l.ItemType == itemType);
        var ids = await links.Select(l => new { l.ItemType, l.ItemId }).ToListAsync(ct);

        var noteIds = ids.Where(i => i.ItemType == ItemTypes.Note).Select(i => i.ItemId).ToList();
        var docIds = ids.Where(i => i.ItemType == ItemTypes.Document).Select(i => i.ItemId).ToList();
        var actIds = ids.Where(i => i.ItemType == ItemTypes.Activity).Select(i => i.ItemId).ToList();
        var me = user.UserId;

        var notes = await (from n in db.Notes where noteIds.Contains(n.Id)
                           join u in db.Users on n.AuthorUserId equals u.Id into us from u in us.DefaultIfEmpty()
                           select new TimelineItem(ItemTypes.Note, n.Id, n.CreatedAt, u == null ? null : u.FirstName + " " + u.LastName,
                               n.Body, n.IsPinned, n.EntityType, n.EntityId, null, null, null, n.AuthorUserId == me)).ToListAsync(ct);
        var docs = await (from d in db.Documents where docIds.Contains(d.Id) && d.ArchivedAt == null
                          join u in db.Users on d.UploadedByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          select new TimelineItem(ItemTypes.Document, d.Id, d.CreatedAt, u == null ? null : u.FirstName + " " + u.LastName,
                              d.FileName, false, d.EntityType, d.EntityId, d.FileName, d.SizeBytes, d.CategoryKey, true)).ToListAsync(ct);
        var acts = await (from a in db.ActivityEvents where actIds.Contains(a.Id)
                          join u in db.Users on a.ActorUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          select new TimelineItem(ItemTypes.Activity, a.Id, a.CreatedAt, u == null ? null : u.FirstName + " " + u.LastName,
                              a.Summary, false, a.EntityType, a.EntityId, null, null, null, false)).ToListAsync(ct);

        var messages = await MessagesAsync(ids.Where(i => i.ItemType == ItemTypes.Email).Select(i => i.ItemId).ToList(),
            ids.Where(i => i.ItemType == ItemTypes.Sms).Select(i => i.ItemId).ToList(), ct);

        return notes.Concat(docs).Concat(acts).Concat(messages)
            .OrderByDescending(i => i.IsPinned).ThenByDescending(i => i.At).Take(take).ToList();
    }

    /// <summary>
    /// Emails and texts for the timeline. Without email/sms view_all you see messages you sent and
    /// replies that came in; teammates' outgoing messages stay private.
    /// </summary>
    private async Task<List<TimelineItem>> MessagesAsync(List<Guid> emailIds, List<Guid> smsIds, CancellationToken ct)
    {
        var result = new List<TimelineItem>();
        var all_ = emailIds.Concat(smsIds).ToList();
        var home = (await db.RecordLinks.Where(l => l.IsPrimary && all_.Contains(l.ItemId)).Select(l => new { l.ItemId, l.EntityType, l.EntityId }).ToListAsync(ct))
            .GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => (g.First().EntityType, g.First().EntityId));
        var me = user.UserId;
        if (emailIds.Count > 0)
        {
            var all = await user.HasPermissionAsync(Oes.Application.Security.Permissions.EmailViewAll, ct);
            var emails = await (from m in db.EmailMessages where emailIds.Contains(m.Id)
                                    && (all || m.SentByUserId == null || m.SentByUserId == me || m.Direction == Domain.Communication.Directions.Inbound)
                                join u in db.Users on m.SentByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                                select new { m.Id, m.SentAt, m.Subject, m.Body, m.Direction, m.Status, m.EntityType, m.EntityId, m.FromAddress,
                                    Sender = u == null ? null : u.FirstName + " " + u.LastName,
                                    To = m.Recipients.Where(r => r.Kind == "to").Select(r => r.Address).FirstOrDefault() }).ToListAsync(ct);
            var shownIds = emails.Select(m => m.Id).ToList();
            var attachments = (await db.EmailAttachments.Where(a => shownIds.Contains(a.EmailMessageId))
                    .Select(a => new { a.EmailMessageId, a.DocumentId, a.FileName }).ToListAsync(ct))
                .GroupBy(a => a.EmailMessageId).ToDictionary(g => g.Key, g => (IReadOnlyList<TimelineAttachment>)g.Select(a => new TimelineAttachment(a.DocumentId, a.FileName)).ToList());
            result.AddRange(emails.Select(m => new TimelineItem(ItemTypes.Email, m.Id, m.SentAt, m.Sender ?? m.FromAddress, Preview(m.Body), false,
                home.GetValueOrDefault(m.Id).EntityType ?? "", home.GetValueOrDefault(m.Id).EntityId, null, null, null, false)
            {
                Message = new TimelineMessage(m.Direction, m.Status, m.Subject, m.Direction == Domain.Communication.Directions.Inbound ? m.FromAddress : m.To ?? "")
                    { Attachments = attachments.GetValueOrDefault(m.Id) ?? [] },
            }));
        }
        if (smsIds.Count > 0)
        {
            var all = await user.HasPermissionAsync(Oes.Application.Security.Permissions.SmsViewAll, ct);
            var texts = await (from m in db.SmsMessages where smsIds.Contains(m.Id)
                                   && (all || m.SentByUserId == null || m.SentByUserId == me || m.Direction == Domain.Communication.Directions.Inbound)
                               join u in db.Users on m.SentByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                               join c in db.Contacts on m.ContactId equals c.Id into cs from c in cs.DefaultIfEmpty()
                               select new { m.Id, m.At, m.Body, m.Direction, m.Status, m.EntityType, m.EntityId, m.FromNumber, m.ToNumber,
                                   Sender = u == null ? null : u.FirstName + " " + u.LastName, Contact = c == null ? null : c.DisplayName }).ToListAsync(ct);
            result.AddRange(texts.Select(m =>
            {
                var inbound = m.Direction == Domain.Communication.Directions.Inbound;
                var number = inbound ? m.FromNumber : m.ToNumber;
                return new TimelineItem(ItemTypes.Sms, m.Id, m.At, inbound ? m.Contact ?? ContactValue.FormatPhone(number, number) : m.Sender, m.Body, false,
                    home.GetValueOrDefault(m.Id).EntityType ?? "", home.GetValueOrDefault(m.Id).EntityId, null, null, null, false)
                { Message = new TimelineMessage(m.Direction, m.Status, null, ContactValue.FormatPhone(number, number)) };
            }));
        }
        return result;
    }

    private static string Preview(string body)
    {
        var s = body.ReplaceLineEndings(" ").Trim();
        return s.Length <= 280 ? s : s[..280] + "…";
    }

    /// <summary>Wraps a stream and feeds everything read through a hash.</summary>
    private sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            hash.AppendData(buffer, offset, n);
            return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            hash.AppendData(buffer.Span[..n]);
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
