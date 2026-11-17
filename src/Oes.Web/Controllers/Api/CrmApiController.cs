using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;

namespace Oes.Web.Controllers.Api;

public sealed record LookupItem(Guid Id, string Label, string? Detail);

/// <summary>Typeahead sources for pickers. Every result is limited to records the user can see.</summary>
[ApiController]
[Route("api/v1/lookup")]
public sealed class LookupController(RecordScope scope, PropertyService properties, Oes.Infrastructure.Persistence.OesDbContext db) : ControllerBase
{
    [HttpGet("contacts")]
    public Task<List<LookupItem>> Contacts(string? q, CancellationToken ct) => ContactsOf(q, null, ct);

    [HttpGet("people")]
    public Task<List<LookupItem>> People(string? q, CancellationToken ct) => ContactsOf(q, ContactTypes.Person, ct);

    [HttpGet("organizations")]
    public Task<List<LookupItem>> Organizations(string? q, CancellationToken ct) => ContactsOf(q, ContactTypes.Organization, ct);

    private async Task<List<LookupItem>> ContactsOf(string? q, string? type, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var term = q.Trim();
        var lower = term.ToLowerInvariant();
        var phone = ContactValue.Phone(term);
        var contacts = await scope.ContactsAsync();
        if (type is not null) contacts = contacts.Where(c => c.Type == type);
        var rows = await contacts
            .Where(c => c.DisplayName.Contains(term) || db.ContactMethods.Any(m => m.ContactId == c.Id &&
                (m.NormalizedValue.Contains(lower) || (phone.Length >= 7 && m.NormalizedValue.Contains(phone)))))
            .OrderBy(c => c.DisplayName).Take(12)
            .Select(c => new
            {
                c.Id, c.DisplayName, c.Type,
                Email = db.ContactMethods.Where(m => m.ContactId == c.Id && m.Kind == ContactMethodKinds.Email).OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                Phone = db.ContactMethods.Where(m => m.ContactId == c.Id && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone)).OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(r => new LookupItem(r.Id, r.DisplayName,
            string.Join(" · ", new[] { r.Type == ContactTypes.Organization ? "Organization" : null, r.Email, r.Phone }.Where(s => s is not null)))).ToList();
    }

    [HttpGet("properties")]
    public async Task<List<LookupItem>> Properties(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var result = await properties.SearchAsync(q, null, 1, 12, ct);
        return result.Items.Select(p => new LookupItem(p.Id, p.Line1 + (p.Line2 is null ? "" : " " + p.Line2),
            p.CityLine + (p.PrimaryContact is null ? "" : " · " + p.PrimaryContact))).ToList();
    }

    /// <summary>Duplicate check while typing an address: exact and near matches.</summary>
    [HttpGet("property-matches")]
    public async Task<List<PropertyMatch>> PropertyMatches([FromQuery] PropertyInput input, CancellationToken ct) =>
        await properties.FindMatchesAsync(input, ct);
}

[ApiController]
[Route("api/v1/search")]
public sealed class SearchController(CrmLookups lookups) : ControllerBase
{
    [HttpGet]
    public Task<List<SearchHit>> Get(string? q, CancellationToken ct) => lookups.SearchAsync(q ?? "", 5, ct);
}

public sealed record MoveStageRequest(Guid StageId, string? Note, string? LostReasonKey);

[ApiController]
[Route("api/v1/opportunities")]
public sealed class OpportunitiesApiController(OpportunityService opportunities) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<OpportunityRow>> List(string? q, Guid? stageId, string? outcome, bool mine = false, int page = 1, CancellationToken ct = default) =>
        await opportunities.SearchAsync(q, stageId, outcome, mine, page, 50, ct);

    /// <summary>Board drag-and-drop. Same rules as the record page (edit rights, lost needs a reason).</summary>
    [HttpPut("{id:guid}/stage")]
    public async Task<IActionResult> MoveStage(Guid id, MoveStageRequest body, CancellationToken ct)
    {
        try
        {
            await opportunities.MoveStageAsync(id, body.StageId, body.Note, body.LostReasonKey, ct);
            return NoContent();
        }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (RecordForbiddenException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (CrmRuleException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest); }
    }
}
