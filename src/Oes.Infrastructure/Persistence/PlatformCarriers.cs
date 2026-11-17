using Oes.Domain.Insurance;

namespace Oes.Infrastructure.Persistence;

/// <summary>
/// The platform's default carrier list. Names only: contact details vary by region and
/// change often, so tenants fill in their own (claims email, fax, portal) on their copy.
/// IDs are fixed so the seed is stable across migrations.
/// </summary>
public static class PlatformCarriers
{
    private static readonly string[] Names =
    [
        "Allstate", "American Family Insurance", "Auto-Owners Insurance", "Chubb", "Cincinnati Insurance",
        "Citizens Property Insurance", "COUNTRY Financial", "Encompass Insurance", "Erie Insurance", "Farmers Insurance",
        "Foremost Insurance", "Grange Insurance", "Hippo", "Homesite", "Kemper", "Lemonade", "Liberty Mutual",
        "Nationwide", "Progressive", "Safeco", "Shelter Insurance", "State Farm", "The Hartford", "Travelers",
        "Universal Property", "USAA", "Westfield",
    ];

    public static IEnumerable<Carrier> Seed()
    {
        var created = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < Names.Length; i++)
        {
            // Stable, readable IDs: 00000000-0000-7000-8000-0000000000NN
            var id = Guid.Parse($"00000000-0000-7000-8000-{i + 1:D12}");
            yield return new Carrier { Id = id, Name = Names[i], CreatedAt = created, UpdatedAt = created };
        }
    }
}
