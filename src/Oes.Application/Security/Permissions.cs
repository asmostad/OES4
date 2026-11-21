namespace Oes.Application.Security;

public sealed record PermissionDefinition(string Key, string Group, string Description);

/// <summary>
/// The permission catalog. Keys are defined here in code and assigned to roles per tenant.
/// Endpoints and pages declare the key they need; nothing checks role names.
/// Adapted from OES3's keys (docs/oes3-reference-data.md), without the dropped features
/// (collections, commissions, weather).
/// </summary>
public static class Permissions
{
    // Records (properties, contacts, opportunities)
    public const string RecordsViewAll = "records.view_all";
    public const string RecordsCreate = "records.create";
    public const string RecordsEditOwn = "records.edit_own";
    public const string RecordsEditAll = "records.edit_all";
    public const string RecordsDelete = "records.delete";
    public const string RecordsReassign = "records.reassign";
    public const string RecordsMerge = "records.merge";

    // Leads and opportunities
    public const string LeadsProspect = "leads.prospect";
    public const string LeadsImport = "leads.import";
    public const string LeadsCampaigns = "leads.campaigns";

    // Insurance
    public const string ClaimsManage = "claims.manage";
    public const string CarriersManage = "carriers.manage";
    public const string StormsManage = "storms.manage";

    // Measurements
    public const string MeasurementsManage = "measurements.manage";

    // Estimates and price lists
    public const string EstimatesCreate = "estimates.create";
    public const string EstimatesViewAll = "estimates.view_all";
    public const string EstimatesManage = "estimates.manage";
    public const string PricingApprove = "pricing.approve";
    public const string PriceListsManage = "price_lists.manage";
    public const string ProductsManage = "products.manage";

    // Production
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsEditOwn = "projects.edit_own";
    public const string ProjectsEditAll = "projects.edit_all";
    public const string WorkflowAdvance = "workflow.advance";
    public const string FinancialsEdit = "financials.edit";
    public const string SubcontractorsManage = "subcontractors.manage";
    public const string WorkOrdersManage = "work_orders.manage";

    // Communication
    public const string EmailSend = "email.send";
    public const string EmailViewAll = "email.view_all";
    public const string SmsSend = "sms.send";
    public const string SmsViewAll = "sms.view_all";
    public const string TemplatesManage = "templates.manage";

    // Tasks and calendar
    public const string TasksCreate = "tasks.create";
    public const string TasksViewAll = "tasks.view_all";
    public const string TasksManage = "tasks.manage";
    public const string CalendarViewAll = "calendar.view_all";
    public const string CalendarManage = "calendar.manage";

    // Marketing
    public const string NurtureManage = "nurture.manage";
    public const string NurtureEnroll = "nurture.enroll";
    public const string BlastsManage = "blasts.manage";
    public const string BookingManage = "booking.manage";

    // Automation and AI
    public const string AutomationManage = "automation.manage";
    public const string AiAssistant = "ai.assistant";
    public const string AiManage = "ai.manage";

    // Reports
    public const string ReportsView = "reports.view";

    // Administration
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(RecordsViewAll, "Records", "View every property, contact and opportunity (otherwise only your own and unassigned)"),
        new(RecordsCreate, "Records", "Create properties, contacts and opportunities"),
        new(RecordsEditOwn, "Records", "Edit records you own"),
        new(RecordsEditAll, "Records", "Edit any record"),
        new(RecordsDelete, "Records", "Archive records"),
        new(RecordsReassign, "Records", "Change a record's owner"),
        new(RecordsMerge, "Records", "Merge duplicate properties and contacts"),

        new(LeadsProspect, "Leads", "Work the prospecting list and set dispositions"),
        new(LeadsImport, "Leads", "Import leads"),
        new(LeadsCampaigns, "Leads", "Manage lead campaigns"),

        new(ClaimsManage, "Insurance", "Create and edit insurance claims and supplements"),
        new(CarriersManage, "Insurance", "Edit the tenant's insurance carrier list"),
        new(StormsManage, "Insurance", "Create and edit storm events"),

        new(MeasurementsManage, "Measurements", "Add and edit property measurements"),

        new(EstimatesCreate, "Estimating", "Create estimates"),
        new(EstimatesViewAll, "Estimating", "View every estimate"),
        new(EstimatesManage, "Estimating", "Edit, issue and void any estimate"),
        new(PricingApprove, "Estimating", "Approve price exceptions on estimates"),
        new(PriceListsManage, "Estimating", "Create, import and edit price lists"),
        new(ProductsManage, "Estimating", "Manage the catalog and products"),

        new(ProjectsCreate, "Production", "Create projects"),
        new(ProjectsEditOwn, "Production", "Edit projects you own"),
        new(ProjectsEditAll, "Production", "Edit any project"),
        new(WorkflowAdvance, "Production", "Move projects and trades between workflow stages"),
        new(FinancialsEdit, "Production", "Edit project financials"),
        new(SubcontractorsManage, "Production", "Manage subcontractors and their rates"),
        new(WorkOrdersManage, "Production", "Create and issue work orders"),

        new(EmailSend, "Communication", "Send email to contacts"),
        new(EmailViewAll, "Communication", "View every user's email on records"),
        new(SmsSend, "Communication", "Send SMS to contacts"),
        new(SmsViewAll, "Communication", "View every user's SMS on records"),
        new(TemplatesManage, "Communication", "Manage email and SMS templates"),

        new(TasksCreate, "Tasks & calendar", "Create tasks"),
        new(TasksViewAll, "Tasks & calendar", "View every user's tasks"),
        new(TasksManage, "Tasks & calendar", "Edit and reassign any task"),
        new(CalendarViewAll, "Tasks & calendar", "View every user's calendar"),
        new(CalendarManage, "Tasks & calendar", "Manage appointments for other users"),

        new(NurtureManage, "Marketing", "Build nurture workflows"),
        new(NurtureEnroll, "Marketing", "Enroll records in nurture workflows"),
        new(BlastsManage, "Marketing", "Send email blasts and manage suppressions"),
        new(BookingManage, "Marketing", "Configure online booking"),

        new(AutomationManage, "Automation & AI", "Manage workflow automation rules"),
        new(AiAssistant, "Automation & AI", "Use the AI assistant"),
        new(AiManage, "Automation & AI", "Manage the AI knowledge base and usage limits"),

        new(ReportsView, "Reports", "View reports"),

        new(UsersManage, "Administration", "Invite, disable and edit users"),
        new(RolesManage, "Administration", "Edit roles and their permissions"),
        new(SettingsManage, "Administration", "Edit company settings"),
        new(AuditView, "Administration", "View the audit log"),
    ];

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);
}

/// <summary>Roles seeded into a new tenant. Tenants can rename and edit them afterwards.</summary>
public static class DefaultRoles
{
    public sealed record Template(string Name, string Description, IReadOnlyList<string> PermissionKeys);

    private static readonly string[] RepBase =
    [
        Permissions.RecordsCreate, Permissions.RecordsEditOwn, Permissions.LeadsProspect,
        Permissions.ClaimsManage, Permissions.MeasurementsManage, Permissions.EstimatesCreate,
        Permissions.ProjectsCreate, Permissions.ProjectsEditOwn, Permissions.EmailSend, Permissions.SmsSend,
        Permissions.TasksCreate, Permissions.NurtureEnroll, Permissions.AiAssistant,
    ];

    private static readonly string[] ManagerBase =
    [
        .. RepBase,
        Permissions.RecordsViewAll, Permissions.RecordsEditAll, Permissions.EstimatesViewAll, Permissions.EstimatesManage,
        Permissions.ProjectsEditAll, Permissions.WorkflowAdvance, Permissions.EmailViewAll, Permissions.SmsViewAll,
        Permissions.TasksViewAll, Permissions.TasksManage, Permissions.CalendarViewAll, Permissions.CalendarManage,
        Permissions.ReportsView,
    ];

    public static readonly IReadOnlyList<Template> All =
    [
        new("Administrator", "Full access to everything in this company", Permissions.All.Select(p => p.Key).ToArray()),
        new("Sales Manager", "Runs the sales team and pipeline",
        [
            .. ManagerBase,
            Permissions.RecordsReassign, Permissions.RecordsMerge, Permissions.LeadsImport, Permissions.LeadsCampaigns,
            Permissions.CarriersManage, Permissions.StormsManage, Permissions.PricingApprove, Permissions.FinancialsEdit,
            Permissions.NurtureManage, Permissions.BlastsManage, Permissions.BookingManage,
        ]),
        new("Production Manager", "Runs production, subs and work orders",
        [
            .. ManagerBase,
            Permissions.ProductsManage, Permissions.PriceListsManage, Permissions.SubcontractorsManage,
            Permissions.WorkOrdersManage,
        ]),
        new("Sales Rep", "Works their own leads, estimates and projects", RepBase),
    ];
}
