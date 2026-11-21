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

public sealed record DispositionRequest(Guid DispositionId, string? Note, DateOnly? CallBackOn);
public sealed record DispositionItem(Guid Id, string Name, string Color, int? RecycleDays, bool IsDoNotContact);
public sealed record NextLead(Guid OpportunityId);
public sealed record CallDto(DateTime At, string Disposition, string Color, string? By, string? Note);
public sealed record ProspectCardDto(Guid Id, string Number, string Name, string StageName, string StageColor, Guid ContactId, string ContactName,
    List<ContactMethodDto> Methods, Guid? PropertyId, string? Address, string? OwnerName, string? StormName, int Attempts,
    string? LastDisposition, List<CallDto> Calls, string? LockedBy, bool CanEdit);

/// <summary>
/// The prospecting call list for the mobile app: claim the next lead (locked to the caller),
/// record the outcome, or release it. Same rules as the web page; errors become ProblemDetails.
/// </summary>
[ApiController]
[Route("api/v1/prospecting")]
public sealed class ProspectingApiController(ProspectingService prospecting) : ControllerBase
{
    [HttpGet("dispositions")]
    public async Task<List<DispositionItem>> Dispositions(CancellationToken ct) =>
        (await prospecting.DispositionsAsync(ct)).Select(d => new DispositionItem(d.Id, d.Name, d.Color, d.RecycleDays, d.IsDoNotContact)).ToList();

    [HttpGet("counts")]
    public Task<ProspectingService.QueueCounts> Counts(Guid? stageId, string? source, Guid? stormId, bool mine = false, Guid? campaignId = null, CancellationToken ct = default) =>
        prospecting.CountsAsync(new ProspectFilter(stageId, source, stormId, mine, campaignId), ct);

    /// <summary>Claims the next due lead; 204 when the list is done.</summary>
    [HttpPost("next")]
    public async Task<IActionResult> Next(Guid? stageId, string? source, Guid? stormId, bool mine = false, Guid? campaignId = null, [FromQuery] Guid[]? skip = null, CancellationToken ct = default) =>
        await prospecting.NextAsync(new ProspectFilter(stageId, source, stormId, mine, campaignId), skip ?? [], ct) is { } id ? Ok(new NextLead(id)) : NoContent();

    /// <summary>The lead on the caller's screen: who to call, the address, and the last calls.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ProspectCardDto> Card(Guid id, CancellationToken ct)
    {
        var c = await prospecting.CardAsync(id, ct);
        var o = c.Opportunity;
        return new ProspectCardDto(o.Id, o.Number, o.Name, c.Stage.Name, c.Stage.Color, c.Contact.Id, c.Contact.DisplayName,
            c.Methods.Select(m => new ContactMethodDto(m.Kind, m.Value, m.IsPrimary)).ToList(), c.Property?.Id, c.Property?.SingleLine,
            c.OwnerName, c.StormName, o.ContactAttempts, c.LastDisposition?.Name,
            c.Calls.Select(x => new CallDto(x.At, x.Disposition, x.Color, x.By, x.Note)).ToList(), c.LockedBy, c.CanEdit);
    }

    [HttpPost("{id:guid}/disposition")]
    public async Task<IActionResult> Disposition(Guid id, DispositionRequest body, CancellationToken ct)
    {
        await prospecting.ApplyAsync(id, body.DispositionId, body.Note, body.CallBackOn, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
    {
        await prospecting.ReleaseAsync(id, ct);
        return NoContent();
    }
}

public sealed record CampaignItem(Guid Id, string Name, string Mode);

/// <summary>Active lead campaigns, so the app can offer them on the call list.</summary>
[ApiController]
[Route("api/v1/campaigns")]
public sealed class CampaignsApiController(CampaignService campaigns) : ControllerBase
{
    [HttpGet]
    public async Task<List<CampaignItem>> Active(CancellationToken ct) =>
        (await campaigns.ActiveAsync(ct)).Select(c => new CampaignItem(c.Id, c.Name, c.Mode)).ToList();
}
