using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;

namespace Oes.Web.Infrastructure;

public sealed class TimelineModel
{
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required List<TimelineItem> Items { get; init; }
    public List<Option> Categories { get; init; } = [];
    public TimelineScope Range { get; init; } = TimelineScope.ThisRecord;
    public string? Filter { get; init; }

    /// <summary>Only records with things under them get the This / Everything toggle.</summary>
    public bool ShowRange => EntityType is EntityTypes.Property or EntityTypes.Contact;

    public static async Task<TimelineModel> LoadAsync(RecordItems items, CrmLookups lookups, string entityType, Guid entityId,
        string? tab, string? range)
    {
        var scope = range == "all" || (range is null && entityType == EntityTypes.Property) ? TimelineScope.Everything : TimelineScope.ThisRecord;
        if (entityType is not (EntityTypes.Property or EntityTypes.Contact)) scope = TimelineScope.ThisRecord;
        var filter = tab is ItemTypes.Note or ItemTypes.Document or ItemTypes.Activity ? tab : null;
        return new TimelineModel
        {
            EntityType = entityType, EntityId = entityId, Range = scope, Filter = filter,
            Items = await items.TimelineAsync(entityType, entityId, scope, filter),
            Categories = await lookups.ValuesAsync(LookupTypes.DocumentCategory),
        };
    }
}
