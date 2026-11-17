using Oes.Application.Security;

namespace Oes.Web.Infrastructure;

/// <param name="Href">Target URL, or null while the page isn't built yet (shown as "Soon").</param>
/// <param name="Permission">Permission needed to see the link, or null for everyone.</param>
/// <param name="Match">Path prefixes that mark this link active.</param>
public sealed record NavItem(string Label, string Icon, string? Href, string? Permission = null, string[]? Match = null)
{
    public bool IsActive(PathString path) =>
        Href is not null && (Match ?? [Href]).Any(m => m == "/" ? path == "/" : path.StartsWithSegments(m));
}

public sealed record NavGroup(string Key, string Title, string Icon, IReadOnlyList<NavItem> Items);

public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items, IReadOnlyList<NavGroup>? Groups = null);

/// <summary>
/// The sidebar, in OES3's order and style. Property, Contacts and Opportunities replace
/// OES3's Consumers/Companies; Insurance gets its own section. Links whose pages arrive
/// in later milestones have a null Href.
/// </summary>
public static class Navigation
{
    public static readonly IReadOnlyList<NavSection> Sections =
    [
        new("Main",
        [
            new("Dashboard", "bi-grid-1x2", "/", Match: ["/"]),
            new("Activity Log", "bi-clock-history", null),
            new("Tasks", "bi-check2-square", null),
            new("Work Orders", "bi-clipboard-check", null, Permissions.WorkOrdersManage),
            new("Calendar", "bi-calendar3", null),
        ]),
        new("CRM",
        [
            new("Properties", "bi-house-door", "/Properties"),
            new("Contacts", "bi-people", "/Contacts"),
            new("Opportunities", "bi-funnel", "/Opportunities"),
            new("Estimates", "bi-file-earmark-text", null),
            new("Price Approvals", "bi-patch-check", null, Permissions.PricingApprove),
        ]),
        new("Insurance",
        [
            new("Claims", "bi-shield-check", null),
            new("Storm Events", "bi-cloud-lightning-rain", null),
        ]),
        new("Production",
        [
            new("Projects", "bi-kanban", null),
            new("Workflow Board", "bi-diagram-3", null),
        ]),
        new("Communication",
        [
            new("Email", "bi-envelope", null),
            new("SMS", "bi-chat-dots", null),
        ]),
        new("Reports",
        [
            new("Reports", "bi-bar-chart-line", null, Permissions.ReportsView),
        ]),
        new("Administration", [],
        [
            new("leads", "Leads & Marketing", "bi-megaphone",
            [
                new("Lead Import", "bi-cloud-upload", null, Permissions.LeadsImport),
                new("Lead Campaigns", "bi-megaphone", null, Permissions.LeadsCampaigns),
                new("Online Booking", "bi-calendar-plus", null, Permissions.BookingManage),
                new("Nurture Workflows", "bi-arrow-repeat", null, Permissions.NurtureManage),
                new("Email Blasts", "bi-send", null, Permissions.BlastsManage),
                new("Email Suppressions", "bi-envelope-slash", null, Permissions.BlastsManage),
            ]),
            new("templates", "Templates & Automation", "bi-lightning-charge",
            [
                new("Email Templates", "bi-envelope-paper", null, Permissions.TemplatesManage),
                new("SMS Templates", "bi-chat-square-text", null, Permissions.TemplatesManage),
                new("Automation", "bi-lightning-charge", null, Permissions.AutomationManage),
            ]),
            new("products", "Products & Estimating", "bi-box-seam",
            [
                new("Catalog", "bi-box-seam", null, Permissions.ProductsManage),
                new("Price Lists", "bi-tags", null, Permissions.PriceListsManage),
                new("Trade Categories", "bi-hammer", null, Permissions.ProductsManage),
                new("Takeoff Rules", "bi-rulers", null, Permissions.ProductsManage),
            ]),
            new("people", "People & Partners", "bi-people",
            [
                new("Users", "bi-person-gear", "/Admin/Users", Permissions.UsersManage),
                new("Roles & Permissions", "bi-shield-lock", "/Admin/Roles", Permissions.RolesManage),
                new("Subcontractors", "bi-people-fill", null, Permissions.SubcontractorsManage),
                new("Insurance Carriers", "bi-shield-check", null, Permissions.CarriersManage),
            ]),
            new("system", "System", "bi-gear",
            [
                new("Settings", "bi-gear", null, Permissions.SettingsManage),
                new("Audit Log", "bi-journal-check", "/Admin/Audit", Permissions.AuditView),
                new("AI Knowledge Base", "bi-journal-text", null, Permissions.AiManage),
                new("AI Usage", "bi-graph-up", null, Permissions.AiManage),
            ]),
        ]),
    ];
}
