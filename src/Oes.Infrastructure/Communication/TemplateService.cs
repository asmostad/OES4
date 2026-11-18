using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Communication;

public sealed class TemplateInput
{
    public string Channel { get; set; } = Channels.Email;
    public string Name { get; set; } = "";
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public string Category { get; set; } = MessagePurposes.Transactional;
    public bool IsActive { get; set; } = true;
}

public sealed class TemplateService(OesDbContext db, RecordScope scope)
{
    public const int SmsMaxLength = 1600;

    public Task<List<MessageTemplate>> ListAsync(string? channel = null, bool activeOnly = false, CancellationToken ct = default)
    {
        var q = db.MessageTemplates.AsQueryable();
        if (channel is not null) q = q.Where(t => t.Channel == channel);
        if (activeOnly) q = q.Where(t => t.IsActive);
        return q.OrderBy(t => t.Channel).ThenBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<MessageTemplate> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new RecordNotFoundException("Template");

    public async Task<Guid> SaveAsync(Guid? id, TemplateInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.TemplatesManage);
        if (!Channels.All.Contains(input.Channel)) throw new CrmRuleException("Pick email or text.");
        if (!MessagePurposes.All.Contains(input.Category)) throw new CrmRuleException("Pick a category.");
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give the template a name (up to 150 characters).");
        var body = input.Body?.Trim() ?? "";
        if (body.Length == 0) throw new CrmRuleException("The message can't be empty.");
        var subject = input.Subject?.Trim();
        if (input.Channel == Channels.Email && string.IsNullOrEmpty(subject)) throw new CrmRuleException("Email templates need a subject.");
        if (input.Channel == Channels.Sms)
        {
            subject = null;
            if (body.Length > SmsMaxLength) throw new CrmRuleException($"Texts can be up to {SmsMaxLength} characters.");
        }
        var unknown = MergeFields.Unknown(subject).Concat(MergeFields.Unknown(body)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new CrmRuleException($"Unknown merge field{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.");
        if (await db.MessageTemplates.AnyAsync(t => t.Channel == input.Channel && t.Name == name && t.Id != id, ct))
            throw new CrmRuleException("There's already a template with that name.");

        var t = id is { } existing ? await GetAsync(existing, ct) : db.MessageTemplates.Add(new MessageTemplate { Channel = input.Channel }).Entity;
        if (t.Channel != input.Channel) throw new CrmRuleException("A template can't change between email and text.");
        t.Name = name; t.Subject = subject; t.Body = body; t.Category = input.Category; t.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.TemplatesManage);
        db.MessageTemplates.Remove(await GetAsync(id, ct)); // sent messages keep their own copy of the text
        await db.SaveChangesAsync(ct);
    }
}
