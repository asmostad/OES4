namespace Oes.Web.Infrastructure;

/// <summary>
/// The record levels. Each gets its own accent, icon and header band so users always know
/// which level they're on (design decision 6).
/// </summary>
public enum RecordLevel { Property, Contact, Organization, Opportunity, Estimate, Project, Trade }

public static class RecordLevels
{
    public static string Icon(this RecordLevel l) => l switch
    {
        RecordLevel.Property => "bi-house-door",
        RecordLevel.Contact => "bi-person",
        RecordLevel.Organization => "bi-building",
        RecordLevel.Opportunity => "bi-funnel",
        RecordLevel.Estimate => "bi-calculator",
        RecordLevel.Project => "bi-hammer",
        RecordLevel.Trade => "bi-layers",
        _ => "bi-circle",
    };

    public static string Label(this RecordLevel l) => l.ToString();

    /// <summary>CSS modifier, e.g. "level-property", which sets --level-accent and friends.</summary>
    public static string Css(this RecordLevel l) => "level-" + l.ToString().ToLowerInvariant();
}

public sealed record Crumb(RecordLevel Level, string Name, string? Href);

public sealed record RecordChip(string Icon, string Text, string? Href);

/// <summary>Model for the shared _RecordHeader partial.</summary>
public sealed class RecordHeaderModel
{
    public required RecordLevel Level { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Number { get; init; }
    public string? Status { get; init; }

    /// <summary>Parent chain from the root (property) down to, but not including, this record.</summary>
    public IReadOnlyList<Crumb> Parents { get; init; } = [];

    public IReadOnlyList<RecordChip> Chips { get; init; } = [];

    public Crumb? Up => Parents.Count > 0 ? Parents[^1] : null;
}
