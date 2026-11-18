using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Web.Controllers.Api;

public sealed record MyDayResponse(int Overdue, int DueToday, IReadOnlyList<TaskRow> Tasks, IReadOnlyList<AppointmentRow> Appointments);
public sealed record DoneRequest(bool Done);
public sealed record NoteRequest(string Body);
public sealed record CreatedResponse(Guid Id);
public sealed record ContactMethodDto(string Kind, string Value, bool IsPrimary);
public sealed record OpportunityDto(Guid Id, string Number, string Name, string StageName, string StageColor, string Outcome,
    decimal? ExpectedValue, string? OwnerName, Guid ContactId, string ContactName, IReadOnlyList<ContactMethodDto> ContactMethods,
    Guid? PropertyId, string? Address, string? Description, bool CanEdit);
public sealed record TimelineDto(string ItemType, Guid Id, DateTime At, string? AuthorName, string Text, string EntityType, Guid EntityId,
    string? FileName, string? Subject, string? Direction);

/// <summary>
/// Endpoints for the field app: my day, tasks, appointments, opportunities, the record timeline,
/// notes and photos. They call the same services as the web pages, so the same record scope,
/// permissions and rules apply; errors come back as ProblemDetails (ApiExceptionFilter).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class MobileController(WorkService work, OpportunityService opportunities, RecordItems items, OesDbContext db) : ControllerBase
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
    public async Task<ActionResult<CreatedResponse>> AddNote(string entityType, Guid id, NoteRequest body, CancellationToken ct)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        return StatusCode(StatusCodes.Status201Created, new CreatedResponse(await items.AddNoteAsync(entityType, id, body.Body ?? "", ct)));
    }

    /// <summary>A photo (or PDF) from the field, filed on the record and everything above it.</summary>
    [HttpPost("records/{entityType}/{id:guid}/photos")]
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
