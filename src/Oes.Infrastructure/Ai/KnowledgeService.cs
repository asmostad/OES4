using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Ai;

public sealed class KnowledgeInput
{
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed record KnowledgeRow(KnowledgeArticle Article, int Passages, string? UpdatedBy);

/// <summary>One search hit: the passage and the article it came from.</summary>
public sealed record KnowledgeHit(Guid ArticleId, string Title, string? Category, string? Heading, string Text, double Score);

/// <summary>
/// The company knowledge base (Administration › AI Knowledge Base, ai.manage): articles the assistant
/// searches with its search_knowledge tool. Searching needs only ai.assistant, since articles are company-wide.
/// </summary>
public sealed class KnowledgeService(OesDbContext db, RecordScope scope, ICurrentUser user, IAuditLog audit)
{
    public const int MaxBodyChars = 100_000;
    public const int MaxArticles = 1000;
    /// <summary>Passages scored per search, after the database narrows them to ones sharing a word.</summary>
    private const int MaxCandidates = 400;

    public async Task<List<KnowledgeRow>> ListAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AiManage);
        return await (from a in db.KnowledgeArticles
                      join u in db.Users on a.UpdatedByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                      orderby a.Category, a.Title
                      select new KnowledgeRow(a, db.KnowledgeChunks.Count(c => c.ArticleId == a.Id), u == null ? null : u.FirstName + " " + u.LastName))
                     .ToListAsync(ct);
    }

    public async Task<KnowledgeArticle> GetAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AiManage);
        return await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new RecordNotFoundException("Article");
    }

    public async Task<List<string>> CategoriesAsync(CancellationToken ct = default) =>
        await db.KnowledgeArticles.Where(a => a.Category != null).Select(a => a.Category!).Distinct().OrderBy(c => c).ToListAsync(ct);

    /// <summary>Creates or updates an article and rebuilds its passages.</summary>
    public async Task<Guid> SaveAsync(Guid? id, KnowledgeInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AiManage);
        var title = (input.Title ?? "").Trim();
        if (title.Length is 0 or > 200) throw new CrmRuleException("Give the article a title (up to 200 characters).");
        var body = (input.Body ?? "").Replace("\r\n", "\n").Trim();
        if (body.Length == 0) throw new CrmRuleException("Write or paste the article's text.");
        if (body.Length > MaxBodyChars) throw new CrmRuleException($"Articles can be up to {MaxBodyChars:#,0} characters. Split this one into a few articles.");
        var category = ContactService.Clean(input.Category);
        if (category is { Length: > 60 }) throw new CrmRuleException("Keep the category under 60 characters.");

        KnowledgeArticle a;
        if (id is { } existing) a = await db.KnowledgeArticles.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new RecordNotFoundException("Article");
        else
        {
            if (await db.KnowledgeArticles.CountAsync(ct) >= MaxArticles) throw new CrmRuleException($"The knowledge base holds up to {MaxArticles:#,0} articles.");
            a = new KnowledgeArticle();
            db.KnowledgeArticles.Add(a);
        }
        a.Title = title;
        a.Category = category;
        a.Body = body;
        a.IsActive = input.IsActive;
        a.UpdatedByUserId = user.UserId;

        db.KnowledgeChunks.RemoveRange(await db.KnowledgeChunks.Where(c => c.ArticleId == a.Id).ToListAsync(ct)); // saved with the article
        foreach (var p in KnowledgeSearch.Split(body))
            db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                ArticleId = a.Id, Seq = p.Seq, Heading = p.Heading is { Length: > 200 } h ? h[..200] : p.Heading, Text = p.Text,
                SearchText = KnowledgeSearch.SearchTextFor(title, p.Heading, p.Text),
            });
        audit.Record(id is null ? "knowledge.created" : "knowledge.updated", "KnowledgeArticle", a.Id, $"Knowledge article {title}");
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = await GetAsync(id, ct);
        db.KnowledgeChunks.RemoveRange(await db.KnowledgeChunks.Where(c => c.ArticleId == id).ToListAsync(ct));
        db.KnowledgeArticles.Remove(a);
        audit.Record("knowledge.deleted", "KnowledgeArticle", id, $"Knowledge article {a.Title}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The best passages for a question, from active articles. The database narrows to passages containing
    /// a query word; <see cref="KnowledgeSearch.Rank"/> scores them.
    /// </summary>
    public async Task<List<KnowledgeHit>> SearchAsync(string query, int take = 5, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AiAssistant);
        var words = KnowledgeSearch.QueryWords(query ?? "");
        if (words.Count == 0) return [];
        var ids = new HashSet<Guid>();
        foreach (var w in words) // rare words first would be better, but a word's count needs a query too; cap instead
        {
            var pattern = $"% {w}%";
            ids.UnionWith(await (from c in db.KnowledgeChunks
                                 join a in db.KnowledgeArticles on c.ArticleId equals a.Id
                                 where a.IsActive && EF.Functions.Like(c.SearchText, pattern)
                                 select c.Id).Take(MaxCandidates).ToListAsync(ct));
        }
        if (ids.Count == 0) return [];
        var rows = await (from c in db.KnowledgeChunks
                          join a in db.KnowledgeArticles on c.ArticleId equals a.Id
                          where ids.Contains(c.Id)
                          select new { c.Id, c.ArticleId, a.Title, a.Category, c.Heading, c.Text, c.SearchText }).ToListAsync(ct);
        var ranked = KnowledgeSearch.Rank(words, rows.Select(r => new KnowledgeCandidate(r.Id, r.ArticleId, r.Title, r.SearchText)).ToList(), take);
        var byId = rows.ToDictionary(r => r.Id);
        return ranked.Select(x => byId[x.Candidate.ChunkId] is var r ? new KnowledgeHit(r.ArticleId, r.Title, r.Category, r.Heading, r.Text, Math.Round(x.Score, 3)) : null!).ToList();
    }
}
