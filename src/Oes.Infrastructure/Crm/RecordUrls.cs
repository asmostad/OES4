using Oes.Domain.Crm;

namespace Oes.Infrastructure.Crm;

/// <summary>Where each record type lives in the web app (for notifications, task lists, agendas).</summary>
public static class RecordUrls
{
    public static string? For(string? entityType, Guid? id) => entityType is null || id is null ? null : entityType switch
    {
        EntityTypes.Property => $"/Properties/View/{id}",
        EntityTypes.Contact => $"/Contacts/View/{id}",
        EntityTypes.Opportunity => $"/Opportunities/View/{id}",
        EntityTypes.Claim => $"/Claims/View/{id}",
        EntityTypes.Estimate => $"/Estimates/View/{id}",
        EntityTypes.Project => $"/Projects/View/{id}",
        EntityTypes.Trade => $"/Projects/Trade/{id}",
        EntityTypes.WorkOrder => $"/WorkOrders/View/{id}",
        _ => null,
    };
}
