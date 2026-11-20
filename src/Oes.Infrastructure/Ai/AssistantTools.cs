using System.Text.Json;
using System.Text.Json.Nodes;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Production;
using Oes.Infrastructure.Reports;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Ai;

/// <summary>
/// The assistant's tools. Each one calls the same service the web page does, as the signed-in user,
/// so record scope and permissions apply to every call: the assistant can never see more than the user.
/// Results are compact JSON; free text from the CRM is trimmed and marked as data.
/// </summary>
public sealed class AssistantTools(
    CrmLookups lookups, OpportunityService opportunities, ContactService contacts, PropertyService properties,
    ProjectService projects, RecordItems items, WorkService work, ReportService reports, TenantTime time, KnowledgeService knowledge)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema);

    public static readonly IReadOnlyList<AiToolDefinition> Definitions =
    [
        new("search_records", "Find contacts, properties, opportunities and projects by name, number, address, phone or email. Returns up to 5 matches per kind with their ids.",
            Schema(new { type = "object", properties = new { query = new { type = "string", description = "Words to search for, e.g. a name, OPP-2026-0007, or part of an address." } }, required = new[] { "query" } })),
        new("get_record", "Read one record: its main details, related records, open tasks and the latest notes and activity. Use an id from search_records or an earlier result.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string", @enum = new[] { "opportunity", "contact", "property", "project" } },
                    id = new { type = "string", description = "The record's id (a GUID)." },
                },
                required = new[] { "type", "id" },
            })),
        new("search_knowledge", "Search the company's own knowledge base: warranties, products, policies, procedures and other things the company wrote down. Returns the best passages with their article titles. Use it before answering questions about how the company does things.",
            Schema(new { type = "object", properties = new { query = new { type = "string", description = "What to look for, in a few words, e.g. \"workmanship warranty length\"." } }, required = new[] { "query" } })),
        new("list_my_tasks", "The user's own tasks, overdue and due first.",
            Schema(new { type = "object", properties = new { include_done = new { type = "boolean", description = "Also list finished tasks." } } })),
        new("my_schedule", "The user's appointments between two dates (company time zone). Defaults to the next 7 days.",
            Schema(new { type = "object", properties = new { from = new { type = "string", description = "yyyy-MM-dd" }, to = new { type = "string", description = "yyyy-MM-dd" } } })),
        new("pipeline_summary", "Open opportunities by stage and owner with expected value (needs report access).",
            Schema(new { type = "object", properties = new { } })),
        new("create_task", "Create a task for the user, optionally on a record. Only when the user asks for one.",
            Schema(new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string" },
                    due_date = new { type = "string", description = "yyyy-MM-dd, optional" },
                    notes = new { type = "string" },
                    record_type = new { type = "string", @enum = new[] { "opportunity", "contact", "property", "project" } },
                    record_id = new { type = "string" },
                },
                required = new[] { "title" },
            })),
    ];

    public sealed record ToolOutcome(string Content, bool IsError, string Status, string Summary);

    /// <summary>What the chat shows while a tool runs.</summary>
    public static string Label(string? tool) => tool switch
    {
        "search_records" => "Searching records",
        "get_record" => "Reading a record",
        "search_knowledge" => "Checking the knowledge base",
        "list_my_tasks" => "Checking your tasks",
        "my_schedule" => "Checking your schedule",
        "pipeline_summary" => "Looking at the pipeline",
        "create_task" => "Creating a task",
        _ => "Looking something up",
    };

    public async Task<ToolOutcome> RunAsync(string name, JsonElement input, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "search_records" => await SearchAsync(Str(input, "query") ?? "", ct),
                "get_record" => await GetRecordAsync(Str(input, "type") ?? "", Id(input, "id"), ct),
                "search_knowledge" => await KnowledgeAsync(Str(input, "query") ?? "", ct),
                "list_my_tasks" => await TasksAsync(Bool(input, "include_done"), ct),
                "my_schedule" => await ScheduleAsync(Date(input, "from"), Date(input, "to"), ct),
                "pipeline_summary" => await PipelineAsync(ct),
                "create_task" => await CreateTaskAsync(input, ct),
                _ => new ToolOutcome($"Unknown tool {name}.", true, "error", "unknown tool"),
            };
        }
        catch (RecordNotFoundException) { return new ToolOutcome("Not found, or this user doesn't have access to it.", true, "denied", "not found or no access"); }
        catch (RecordForbiddenException ex) { return new ToolOutcome($"Not allowed: {ex.Message}", true, "denied", ex.Message); }
        catch (CrmRuleException ex) { return new ToolOutcome(ex.Message, true, "error", ex.Message); }
        catch (FormatException) { return new ToolOutcome("Bad input: ids are GUIDs and dates are yyyy-MM-dd.", true, "error", "bad input"); }
    }

    private static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static Guid Id(JsonElement e, string key) => Guid.Parse(Str(e, key) ?? throw new FormatException());
    private static DateOnly? Date(JsonElement e, string key) => Str(e, key) is { Length: > 0 } s ? DateOnly.ParseExact(s, "yyyy-MM-dd") : null;
    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max] + "…";
    private static ToolOutcome Ok(object data, string summary) => new(JsonSerializer.Serialize(data, Json), false, "ok", summary);

    private async Task<ToolOutcome> SearchAsync(string q, CancellationToken ct)
    {
        if (q.Trim().Length < 2) return new ToolOutcome("Search for at least 2 characters.", true, "error", "query too short");
        var hits = await lookups.SearchAsync(q.Trim(), 5, ct);
        return Ok(hits.Select(h => new { kind = h.Kind, id = h.Id, title = h.Title, detail = h.Subtitle }), $"searched \"{Cut(q, 40)}\": {hits.Count} found");
    }

    private async Task<ToolOutcome> KnowledgeAsync(string q, CancellationToken ct)
    {
        if (q.Trim().Length < 2) return new ToolOutcome("Search for at least 2 characters.", true, "error", "query too short");
        var hits = await knowledge.SearchAsync(q, 5, ct);
        if (hits.Count == 0) return Ok(new { found = 0, note = "Nothing in the knowledge base matches. Say so; don't guess the company's policy." }, $"knowledge \"{Cut(q, 40)}\": nothing");
        return Ok(hits.Select(h => new { article = h.Title, h.Category, section = h.Heading, passage = h.Text }),
            $"knowledge \"{Cut(q, 40)}\": {hits.Count} passages from {string.Join(", ", hits.Select(h => h.Title).Distinct().Take(3))}");
    }

    private async Task<object> TimelineAsync(string type, Guid id, CancellationToken ct) =>
        (await items.TimelineAsync(type, id, TimelineScope.Everything, null, 10, ct))
        .Select(i => new { kind = i.ItemType, at = i.At, by = i.AuthorName, text = Cut(i.Message?.Subject is { } s ? $"{s}: {i.Text}" : i.Text, 400) });

    private async Task<object> OpenTasksAsync(string type, Guid id, CancellationToken ct) =>
        (await work.TasksAsync(false, false, type, id, ct: ct)).Take(10).Select(t => new { t.Title, due = t.DueLocal, t.AssigneeName, t.IsOverdue });

    private async Task<ToolOutcome> GetRecordAsync(string type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case EntityTypes.Opportunity:
            {
                var d = await opportunities.GetAsync(id, ct);
                var o = d.Opportunity;
                return Ok(new
                {
                    type, o.Id, o.Number, o.Name, stage = d.Stage.Name, o.Outcome, o.ExpectedValue, owner = d.OwnerName, o.IsInsurance,
                    contact = new { d.Contact.Id, d.Contact.DisplayName }, property = d.Property is null ? null : new { d.Property.Id, address = d.Property.SingleLine },
                    description = Cut(o.Description, 600), created = o.CreatedAt, stageSince = o.StageChangedAt,
                    openTasks = await OpenTasksAsync(type, id, ct), recent = await TimelineAsync(type, id, ct),
                }, $"read {o.Number}");
            }
            case EntityTypes.Contact:
            {
                var d = await contacts.GetAsync(id, ct);
                return Ok(new
                {
                    type, d.Contact.Id, name = d.Contact.DisplayName, d.Contact.Status, owner = d.OwnerName,
                    methods = d.Methods.Select(m => new { m.Kind, m.Value, m.IsPrimary }),
                    properties = d.Properties.Select(p => new { p.PropertyId, p.Address, p.RoleKey }),
                    opportunities = d.Opportunities.Select(o => new { o.Id, o.Number, o.Name, stage = o.StageName, o.Outcome }),
                    notes = Cut(d.Contact.Notes, 600), openTasks = await OpenTasksAsync(type, id, ct), recent = await TimelineAsync(type, id, ct),
                }, $"read contact {d.Contact.DisplayName}");
            }
            case EntityTypes.Property:
            {
                var d = await properties.GetAsync(id, ct);
                return Ok(new
                {
                    type, d.Property.Id, address = d.Property.SingleLine, kind = d.Property.Kind, notes = Cut(d.Property.Notes, 600),
                    contacts = d.Contacts.Where(c => c.IsCurrent).Select(c => new { c.ContactId, c.Name, c.RoleKey, c.Phone, c.Email }),
                    opportunities = d.Opportunities.Select(o => new { o.Id, o.Number, o.Name, stage = o.StageName, o.Outcome }),
                    openTasks = await OpenTasksAsync(type, id, ct), recent = await TimelineAsync(type, id, ct),
                }, $"read property {d.Property.Line1}");
            }
            case EntityTypes.Project:
            {
                var d = await projects.GetAsync(id, ct);
                var p = d.Project;
                return Ok(new
                {
                    type, p.Id, p.Number, p.Name, stage = d.Stage.Name, contract = p.ContractValue, manager = d.OwnerName, completed = p.CompletedAt,
                    customer = new { d.Contact.Id, d.Contact.DisplayName }, address = d.Property?.SingleLine,
                    trades = d.Trades.Select(t => new { trade = t.Type.Name, stage = t.Stage.Name, t.Trade.Amount, t.Trade.WorkDate, t.IsStale }),
                    openTasks = await OpenTasksAsync(type, id, ct), recent = await TimelineAsync(type, id, ct),
                }, $"read {p.Number}");
            }
            default:
                return new ToolOutcome("Type must be opportunity, contact, property or project.", true, "error", "bad type");
        }
    }

    private async Task<ToolOutcome> TasksAsync(bool includeDone, CancellationToken ct)
    {
        var tasks = await work.TasksAsync(onlyMine: true, includeDone: includeDone, ct: ct);
        return Ok(tasks.Take(40).Select(t => new { t.Title, due = t.DueLocal, t.IsOverdue, t.Priority, done = t.CompletedAt != null, t.EntityType, t.EntityId }),
            $"{tasks.Count} tasks");
    }

    private async Task<ToolOutcome> ScheduleAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var start = from ?? await time.TodayAsync();
        var end = to ?? start.AddDays(6);
        if (end < start || end.DayNumber - start.DayNumber > 31) return new ToolOutcome("Ask for up to 31 days.", true, "error", "range too long");
        var rows = await work.AppointmentsAsync(start, end, null, ct: ct);
        return Ok(rows.Select(a => new { a.Title, start = a.StartLocal, end = a.EndLocal, a.Location, with = a.AssigneeName, a.Status, a.EntityType, a.EntityId }),
            $"{rows.Count} appointments {start:MMM d}–{end:MMM d}");
    }

    private async Task<ToolOutcome> PipelineAsync(CancellationToken ct)
    {
        var today = await time.TodayAsync();
        var r = await reports.PipelineAsync(new ReportFilter(today, today), ct);
        var byStage = r.Tables[0];
        return Ok(new
        {
            open = r.Kpis[0].Value, expectedValue = r.Kpis[1].Value,
            stages = byStage.Rows.Select(x => new { stage = x[0], count = x[1], value = x[2] }),
            owners = r.Tables[1].Rows.Select(x => new { owner = x[0], count = x[1], value = x[2] }),
        }, "pipeline summary");
    }

    private async Task<ToolOutcome> CreateTaskAsync(JsonElement input, CancellationToken ct)
    {
        var type = Str(input, "record_type");
        var id = Str(input, "record_id") is { Length: > 0 } s ? Guid.Parse(s) : (Guid?)null;
        var taskId = await work.CreateTaskAsync(new TaskInput
        {
            Title = Cut(Str(input, "title"), 300) ?? "", Notes = Cut(Str(input, "notes"), 2000), DueDate = Date(input, "due_date"),
            EntityType = id is null ? null : type, EntityId = id,
        }, ct);
        return Ok(new { created = true, id = taskId }, $"created task \"{Cut(Str(input, "title"), 60)}\"");
    }
}
