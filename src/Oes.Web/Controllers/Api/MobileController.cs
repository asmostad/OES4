using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record MyDayResponse(int Overdue, int DueToday, IReadOnlyList<TaskRow> Tasks, IReadOnlyList<AppointmentRow> Appointments);
public sealed record DoneRequest(bool Done);
public sealed record NoteRequest(string Body);
public sealed record CreatedResponse(Guid Id);
public sealed record ContactMethodDto(string Kind, string Value, bool IsPrimary);
public sealed record OpportunityDto(Guid Id, string Number, string Name, string StageName, string StageColor, string Outcome,
    decimal? ExpectedValue, string? OwnerName, Guid ContactId, string ContactName, IReadOnlyList<ContactMethodDto> ContactMethods,
    Guid? PropertyId, string? Address, string? Description, bool CanEdit);
public sealed record StageOption(Guid Id, string Name, string Color, string Outcome);
public sealed record LinkDto(Guid Id, string Label, string? Role);
public sealed record OpportunityBriefDto(Guid Id, string Number, string Name, string StageName, string StageColor, string Outcome, string? Address);
public sealed record ContactDto(Guid Id, string Name, string Type, string? OwnerName, List<ContactMethodDto> Methods, List<LinkDto> Properties,
    List<OpportunityBriefDto> Opportunities, bool CanEdit);
public sealed record PropertyContactDto(Guid Id, string Name, string Role, bool IsPrimary, string? Phone, string? Email);
public sealed record PropertyDto(Guid Id, string Address, List<PropertyContactDto> Contacts, List<OpportunityBriefDto> Opportunities);
public sealed record OptionsResponse(List<StageOption> Stages, List<Option> LostReasons, List<Option> AppointmentTypes, List<Option> LeadSources);
public sealed record DeviceRequest(string Token, string Platform);
public sealed record UnregisterDeviceRequest(string Token);
public sealed record DocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string? Category, DateTime CreatedAt);
public sealed record TimelineDto(string ItemType, Guid Id, DateTime At, string? AuthorName, string Text, string EntityType, Guid EntityId,
    string? FileName, string? Subject, string? Direction);

/// <summary>
/// Endpoints for the field app: my day, tasks, appointments, opportunities, the record timeline,
/// notes and photos. They call the same services as the web pages, so the same record scope,
/// permissions and rules apply; errors come back as ProblemDetails (ApiExceptionFilter).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class MobileController(WorkService work, OpportunityService opportunities, RecordItems items, OesDbContext db, PushService push,
    CrmLookups lookups, FieldLeadService leads, ContactService contacts, PropertyService properties) : ControllerBase
{
    public const long MaxPhotoBytes = 25 * 1024 * 1024;

    [HttpGet("my-day")]
    public async Task<MyDayResponse> MyDay(CancellationToken ct)
    {
        var day = await work.MyDayAsync(ct);
        var tasks = (await work.TasksAsync(onlyMine: true, includeDone: false, ct: ct)).Where(t => t.IsOverdue || t.IsDueToday).ToList();
        return new MyDayResponse(day.Overdue, day.DueToday, tasks, day.Appointments);
    }

    [HttpGet("tasks")]
    public Task<List<TaskRow>> Tasks(bool mine = true, bool done = false, CancellationToken ct = default) =>
        work.TasksAsync(onlyMine: mine, includeDone: done, ct: ct);

    [HttpPost("tasks")]
    [Idempotent]
    public async Task<ActionResult<CreatedResponse>> CreateTask(TaskInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new CreatedResponse(await work.CreateTaskAsync(input, ct)));

    [HttpPut("tasks/{id:guid}/done")]
    public async Task<IActionResult> SetDone(Guid id, DoneRequest body, CancellationToken ct)
    {
        await work.SetDoneAsync(id, body.Done, ct);
        return NoContent();
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<List<AppointmentRow>>> Appointments(DateOnly from, DateOnly to, Guid? user, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62) return Problem("Ask for up to 62 days at a time.", statusCode: StatusCodes.Status400BadRequest);
        return await work.AppointmentsAsync(from, to, user, ct: ct);
    }

    // The opportunity list is GET api/v1/opportunities (OpportunitiesApiController), shared with the web board.

    [HttpGet("opportunities/{id:guid}")]
    public async Task<OpportunityDto> Opportunity(Guid id, CancellationToken ct)
    {
        var d = await opportunities.GetAsync(id, ct);
        var methods = await db.ContactMethods.Where(m => m.ContactId == d.Contact.Id).OrderByDescending(m => m.IsPrimary)
            .Select(m => new ContactMethodDto(m.Kind, m.Value, m.IsPrimary)).ToListAsync(ct);
        var o = d.Opportunity;
        return new OpportunityDto(o.Id, o.Number, o.Name, d.Stage.Name, d.Stage.Color, o.Outcome, o.ExpectedValue, d.OwnerName,
            d.Contact.Id, d.Contact.DisplayName, methods, d.Property?.Id, d.Property?.SingleLine, o.Description, d.CanEdit);
    }

    [HttpGet("records/{entityType}/{id:guid}/timeline")]
    public async Task<ActionResult<List<TimelineDto>>> Timeline(string entityType, Guid id, bool everything = true, CancellationToken ct = default)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        var list = await items.TimelineAsync(entityType, id, everything ? TimelineScope.Everything : TimelineScope.ThisRecord, null, 100, ct);
        return list.Select(i => new TimelineDto(i.ItemType, i.Id, i.At, i.AuthorName, i.Text, i.EntityType, i.EntityId, i.FileName,
            i.Message?.Subject, i.Message?.Direction)).ToList();
    }

    [HttpPost("records/{entityType}/{id:guid}/notes")]
    [Idempotent]
    public async Task<ActionResult<CreatedResponse>> AddNote(string entityType, Guid id, NoteRequest body, CancellationToken ct)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        return StatusCode(StatusCodes.Status201Created, new CreatedResponse(await items.AddNoteAsync(entityType, id, body.Body ?? "", ct)));
    }

    /// <summary>A contact for the app: how to reach them, their properties and their leads (the same scope as the web page).</summary>
    [HttpGet("contacts/{id:guid}")]
    public async Task<ContactDto> Contact(Guid id, CancellationToken ct)
    {
        var d = await contacts.GetAsync(id, ct);
        return new ContactDto(d.Contact.Id, d.Contact.DisplayName, d.Contact.Type, d.OwnerName,
            d.Methods.OrderByDescending(m => m.IsPrimary).Select(m => new ContactMethodDto(m.Kind, m.Value, m.IsPrimary)).ToList(),
            d.Properties.Where(p => p.IsCurrent).Select(p => new LinkDto(p.PropertyId, p.Address, p.RoleKey)).ToList(),
            d.Opportunities.Select(Brief).ToList(), d.CanEdit);
    }

    /// <summary>A property for the app: who's on it and its leads.</summary>
    [HttpGet("properties/{id:guid}")]
    public async Task<PropertyDto> Property(Guid id, CancellationToken ct)
    {
        var d = await properties.GetAsync(id, ct);
        return new PropertyDto(d.Property.Id, d.Property.SingleLine,
            d.Contacts.Where(c => c.IsCurrent).Select(c => new PropertyContactDto(c.ContactId, c.Name, c.RoleKey, c.IsPrimary, c.Phone, c.Email)).ToList(),
            d.Opportunities.Select(Brief).ToList());
    }

    private static OpportunityBriefDto Brief(OpportunityBrief o) => new(o.Id, o.Number, o.Name, o.StageName, o.StageColor, o.Outcome, o.Address);

    /// <summary>The choices the app's forms need: pipeline stages, lost reasons, appointment types and lead sources.</summary>
    [HttpGet("options")]
    public async Task<OptionsResponse> Options(CancellationToken ct)
    {
        var stages = await db.PipelineStages.Where(s => s.IsActive).OrderBy(s => s.Sort)
            .Select(s => new StageOption(s.Id, s.Name, s.Color, s.Outcome)).ToListAsync(ct);
        return new OptionsResponse(stages, await lookups.ValuesAsync(LookupTypes.LostReason, ct),
            await lookups.ValuesAsync(Oes.Domain.Work.AppointmentTypes.LookupType, ct), await lookups.ValuesAsync(LookupTypes.LeadSource, ct));
    }

    /// <summary>A lead taken in the field (door knock by default): contact, opportunity owned by the user, and address.</summary>
    [HttpPost("leads")]
    [Idempotent]
    public async Task<ActionResult<FieldLeadResult>> CreateLead(FieldLeadInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await leads.CreateAsync(input, ct));

    /// <summary>An appointment on the user's calendar (or someone's, with calendar.manage), usually on a lead.</summary>
    [HttpPost("appointments")]
    [Idempotent]
    public async Task<ActionResult<CreatedResponse>> CreateAppointment(AppointmentInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new CreatedResponse(await work.CreateAppointmentAsync(input, ct)));

    /// <summary>This phone's Expo push address, after the user allowed notifications. Calling again refreshes it.</summary>
    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice(DeviceRequest body, CancellationToken ct)
    {
        await push.RegisterAsync(body.Token, body.Platform, ct);
        return NoContent();
    }

    /// <summary>Stop pushing to this phone (the app calls it when signing out).</summary>
    [HttpPost("devices/unregister")]
    public async Task<IActionResult> UnregisterDevice(UnregisterDeviceRequest body, CancellationToken ct)
    {
        await push.UnregisterAsync(body.Token, ct);
        return NoContent();
    }

    /// <summary>
    /// Files on the record and the records under it (an opportunity's estimates, a project's work orders), newest first,
    /// only ones this user can open. Saved PDFs carry their category: proposal, signed_proposal, change_order,
    /// order_sheet, work_order. Download with <c>GET documents/{id}</c>.
    /// </summary>
    [HttpGet("records/{entityType}/{id:guid}/documents")]
    public async Task<ActionResult<List<DocumentDto>>> Documents(string entityType, Guid id, int take = 50, CancellationToken ct = default)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        return (await items.AttachableDocumentsAsync(entityType, id, Math.Clamp(take, 1, 200), ct))
            .Select(d => new DocumentDto(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.CategoryKey, d.CreatedAt)).ToList();
    }

    /// <summary>A stored file's content, through the same visibility check as the web download.</summary>
    [HttpGet("documents/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (doc, content) = await items.OpenDocumentAsync(id, ct);
        return File(content, string.IsNullOrEmpty(doc.ContentType) ? "application/octet-stream" : doc.ContentType, doc.FileName);
    }

    /// <summary>A photo (or PDF) from the field, filed on the record and everything above it.</summary>
    [HttpPost("records/{entityType}/{id:guid}/photos")]
    [Idempotent]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<ActionResult<CreatedResponse>> UploadPhoto(string entityType, Guid id, IFormFile file, CancellationToken ct)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        if (file is null || file.Length == 0) return Problem("Attach a photo.", statusCode: StatusCodes.Status400BadRequest);
        if (!file.ContentType.StartsWith("image/") && file.ContentType != "application/pdf")
            return Problem("Photos must be images (or PDF).", statusCode: StatusCodes.Status415UnsupportedMediaType);
        await using var stream = file.OpenReadStream();
        var docId = await items.UploadAsync(entityType, id, stream, Path.GetFileName(file.FileName), file.ContentType, "photos", ct);
        return StatusCode(StatusCodes.Status201Created, new CreatedResponse(docId));
    }
}
