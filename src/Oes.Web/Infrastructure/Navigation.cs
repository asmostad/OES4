using Oes.Application.Security;

namespace Oes.Web.Infrastructure;

/// <param name="Href">Target URL, or null while the page isn't built yet (shown as "Soon").</param>
/// <param name="Permission">Permission needed to see the link, or null for everyone.</param>
/// <param name="Match">Path prefixes that mark this link active.</param>
public sealed record NavItem(string Label, string Icon, string? Href, string? Permission = null, string[]? Match = null)
{
    public bool IsActive(PathString path) =>
        Href is not null && (path.Equals(Href, StringComparison.OrdinalIgnoreCase)
                             || (Match ?? [Href]).Any(m => m == "/" ? path == "/" : path.StartsWithSegments(m)));
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
            new("Tasks", "bi-check2-square", "/Tasks"),
            new("Work Orders", "bi-clipboard-check", "/WorkOrders", Permissions.WorkOrdersManage),
            new("Calendar", "bi-calendar3", "/Calendar"),
            new("Bookings", "bi-calendar-check", "/Bookings", Permissions.BookingManage),
            new("Assistant", "bi-stars", "/Assistant", Permissions.AiAssistant),
        ]),
        new("CRM",
        [
            new("Properties", "bi-house-door", "/Properties"),
            new("Contacts", "bi-people", "/Contacts"),
            new("Opportunities", "bi-funnel", "/Opportunities"),
            new("Prospecting", "bi-telephone-outbound", "/Prospecting", Permissions.LeadsProspect),
            new("Measurements", "bi-rulers", "/Measurements"),
            new("Estimates", "bi-file-earmark-text", "/Estimates", Match: ["/Estimates/View", "/Estimates/New", "/Estimates/Index"]),
            new("Price Approvals", "bi-patch-check", "/Estimates/Approvals", Permissions.PricingApprove),
        ]),
        new("Insurance",
        [
            new("Claims", "bi-shield-check", "/Claims"),
            new("Storm Events", "bi-cloud-lightning-rain", "/Storms"),
        ]),
        new("Production",
        [
            new("Projects", "bi-kanban", "/Projects"),
            new("Workflow Board", "bi-diagram-3", "/Workflow"),
        ]),
        new("Communication",
        [
            new("Email", "bi-envelope", null),
            new("SMS", "bi-chat-dots", null),
            new("Unmatched Texts", "bi-chat-square-dots", "/Messages/Unmatched", Permissions.SmsViewAll),
        ]),
        new("Reports",
        [
            new("Reports", "bi-bar-chart-line", "/Reports", Permissions.ReportsView),
        ]),
        new("Administration", [],
        [
            new("leads", "Leads & Marketing", "bi-megaphone",
            [
                new("Lead Import", "bi-cloud-upload", null, Permissions.LeadsImport),
                new("Lead Campaigns", "bi-megaphone", "/Campaigns", Permissions.LeadsCampaigns),
                new("Online Booking", "bi-calendar-plus", "/Admin/BookingTypes", Permissions.BookingManage),
                new("Nurture Workflows", "bi-arrow-repeat", "/Admin/Nurture", Permissions.NurtureManage),
                new("Email Blasts", "bi-send", "/Blasts", Permissions.BlastsManage),
                new("Email Suppressions", "bi-envelope-slash", "/Admin/Suppressions", Permissions.BlastsManage),
            ]),
            new("templates", "Templates & Automation", "bi-lightning-charge",
            [
                new("Email Templates", "bi-envelope-paper", "/Admin/Templates/email", Permissions.TemplatesManage),
                new("Texting & SMS Templates", "bi-chat-square-text", "/Admin/Templates/sms", Permissions.TemplatesManage),
                new("Automation", "bi-lightning-charge", "/Admin/Automation", Permissions.AutomationManage),
            ]),
            new("products", "Products & Estimating", "bi-box-seam",
            [
                new("Products", "bi-box-seam", "/Admin/Products", Permissions.ProductsManage),
                new("Price Lists", "bi-tags", "/PriceLists", Permissions.PriceListsManage),
                new("Trades & Measurements", "bi-hammer", "/Admin/Trades", Permissions.ProductsManage),
                new("Takeoff Rules", "bi-rulers", "/Admin/Takeoff", Permissions.ProductsManage, Match: ["/Admin/Takeoff", "/Admin/TakeoffRule"]),
            ]),
            new("people", "People & Partners", "bi-people",
            [
                new("Users", "bi-person-gear", "/Admin/Users", Permissions.UsersManage),
                new("Roles & Permissions", "bi-shield-lock", "/Admin/Roles", Permissions.RolesManage),
                new("Subcontractors", "bi-people-fill", "/Subcontractors", Permissions.SubcontractorsManage),
                new("Insurance Carriers", "bi-shield-check", "/Admin/Carriers", Permissions.CarriersManage),
            ]),
            new("system", "System", "bi-gear",
            [
                new("Pipeline & Lists", "bi-list-check", "/Admin/Lists", Permissions.SettingsManage),
                new("Settings", "bi-gear", "/Admin/Settings", Permissions.SettingsManage),
                new("Audit Log", "bi-journal-check", "/Admin/Audit", Permissions.AuditView),
                new("AI Knowledge Base", "bi-journal-text", null, Permissions.AiManage),
                new("AI Usage", "bi-graph-up", "/Admin/AiUsage", Permissions.AiManage),
            ]),
        ]),
    ];
}
