using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Insurance;
using Oes.Domain.Measurements;
using Oes.Domain.Production;
using Oes.Domain.Subcontractors;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Identity;

namespace Oes.Infrastructure.Persistence;

public class OesDbContext(DbContextOptions<OesDbContext> options, ITenantContext tenant)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>Name of the global tenant filter, for the rare audited IgnoreQueryFilters([TenantFilter]) call.</summary>
    public const string TenantFilter = "Tenant";

    public ITenantContext Tenant { get; } = tenant;

    /// <summary>Read by the tenant query filter on every query. Guid.Empty matches nothing.</summary>
    public Guid CurrentTenantId => Tenant.TenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();

    // CRM core
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactMethod> ContactMethods => Set<ContactMethod>();
    public DbSet<ContactAddress> ContactAddresses => Set<ContactAddress>();
    public DbSet<ContactRelationship> ContactRelationships => Set<ContactRelationship>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyContact> PropertyContacts => Set<PropertyContact>();
    public DbSet<PropertyStructure> PropertyStructures => Set<PropertyStructure>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OpportunityStageChange> OpportunityStageChanges => Set<OpportunityStageChange>();
    public DbSet<Disposition> Dispositions => Set<Disposition>();
    public DbSet<OpportunityDisposition> OpportunityDispositions => Set<OpportunityDisposition>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<Domain.Reports.SavedReport> SavedReports => Set<Domain.Reports.SavedReport>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignMember> CampaignMembers => Set<CampaignMember>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<RecordLink> RecordLinks => Set<RecordLink>();

    // Insurance
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<TenantCarrier> TenantCarriers => Set<TenantCarrier>();
    public DbSet<StormEvent> StormEvents => Set<StormEvent>();
    public DbSet<InsuranceClaim> Claims => Set<InsuranceClaim>();
    public DbSet<Supplement> Supplements => Set<Supplement>();

    // Measurements
    public DbSet<TradeType> TradeTypes => Set<TradeType>();
    public DbSet<MeasurementFieldDef> MeasurementFieldDefs => Set<MeasurementFieldDef>();
    public DbSet<MeasurementSet> MeasurementSets => Set<MeasurementSet>();
    public DbSet<MeasurementValue> MeasurementValues => Set<MeasurementValue>();

    // Estimating
    public DbSet<CatalogCategory> CatalogCategories => Set<CatalogCategory>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TakeoffRule> TakeoffRules => Set<TakeoffRule>();
    public DbSet<Estimate> Estimates => Set<Estimate>();
    public DbSet<EstimateSection> EstimateSections => Set<EstimateSection>();
    public DbSet<EstimateLine> EstimateLines => Set<EstimateLine>();
    public DbSet<EstimateVersion> EstimateVersions => Set<EstimateVersion>();

    // Production
    public DbSet<WorkflowStage> WorkflowStages => Set<WorkflowStage>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTrade> ProjectTrades => Set<ProjectTrade>();
    public DbSet<OrderSheetLine> OrderSheetLines => Set<OrderSheetLine>();
    public DbSet<WorkflowStageChange> WorkflowStageChanges => Set<WorkflowStageChange>();
    public DbSet<ChangeOrder> ChangeOrders => Set<ChangeOrder>();
    public DbSet<ChangeOrderLine> ChangeOrderLines => Set<ChangeOrderLine>();

    // Subcontractors
    public DbSet<Subcontractor> Subcontractors => Set<Subcontractor>();
    public DbSet<SubcontractorDocument> SubcontractorDocuments => Set<SubcontractorDocument>();
    public DbSet<SubcontractorRate> SubcontractorRates => Set<SubcontractorRate>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderLine> WorkOrderLines => Set<WorkOrderLine>();
    public DbSet<ExternalAccessToken> ExternalAccessTokens => Set<ExternalAccessToken>();

    // Tasks, calendar, notifications
    public DbSet<Domain.Work.TaskItem> Tasks => Set<Domain.Work.TaskItem>();
    public DbSet<Domain.Work.Appointment> Appointments => Set<Domain.Work.Appointment>();
    public DbSet<Domain.Work.AppointmentAttendee> AppointmentAttendees => Set<Domain.Work.AppointmentAttendee>();
    public DbSet<Domain.Work.Notification> Notifications => Set<Domain.Work.Notification>();
    public DbSet<Domain.Work.AutomationRule> AutomationRules => Set<Domain.Work.AutomationRule>();
    public DbSet<Domain.Work.AutomationRun> AutomationRuns => Set<Domain.Work.AutomationRun>();
    public DbSet<Domain.Ai.AiConversation> AiConversations => Set<Domain.Ai.AiConversation>();
    public DbSet<Domain.Ai.AiChatMessage> AiMessages => Set<Domain.Ai.AiChatMessage>();
    public DbSet<Domain.Ai.AiToolCall> AiToolCalls => Set<Domain.Ai.AiToolCall>();
    public DbSet<Domain.Ai.AiUsageDaily> AiUsageDaily => Set<Domain.Ai.AiUsageDaily>();
    public DbSet<ScopeImport> ScopeImports => Set<ScopeImport>();

    public DbSet<Domain.Communication.MessageTemplate> MessageTemplates => Set<Domain.Communication.MessageTemplate>();
    public DbSet<Domain.Communication.EmailMessage> EmailMessages => Set<Domain.Communication.EmailMessage>();
    public DbSet<Domain.Communication.EmailRecipient> EmailRecipients => Set<Domain.Communication.EmailRecipient>();
    public DbSet<Domain.Communication.SmsMessage> SmsMessages => Set<Domain.Communication.SmsMessage>();
    public DbSet<Domain.Communication.ContactConsent> ContactConsents => Set<Domain.Communication.ContactConsent>();
    public DbSet<Domain.Communication.Suppression> Suppressions => Set<Domain.Communication.Suppression>();
    public DbSet<Domain.Communication.TenantPhoneNumber> TenantPhoneNumbers => Set<Domain.Communication.TenantPhoneNumber>();
    public DbSet<Domain.Communication.Blast> Blasts => Set<Domain.Communication.Blast>();
    public DbSet<Domain.Communication.BlastRecipient> BlastRecipients => Set<Domain.Communication.BlastRecipient>();
    public DbSet<Domain.Communication.Sequence> Sequences => Set<Domain.Communication.Sequence>();
    public DbSet<Domain.Communication.SequenceStep> SequenceSteps => Set<Domain.Communication.SequenceStep>();
    public DbSet<Domain.Communication.Enrollment> Enrollments => Set<Domain.Communication.Enrollment>();
    public DbSet<Domain.Communication.EnrollmentStepRun> EnrollmentStepRuns => Set<Domain.Communication.EnrollmentStepRun>();

    public DbSet<Domain.Booking.BookingType> BookingTypes => Set<Domain.Booking.BookingType>();
    public DbSet<Domain.Booking.BookingTypeHost> BookingTypeHosts => Set<Domain.Booking.BookingTypeHost>();
    public DbSet<Domain.Booking.AvailabilityRule> AvailabilityRules => Set<Domain.Booking.AvailabilityRule>();
    public DbSet<Domain.Booking.BusyCalendar> BusyCalendars => Set<Domain.Booking.BusyCalendar>();
    public DbSet<Domain.Booking.BusyBlock> BusyBlocks => Set<Domain.Booking.BusyBlock>();
    public DbSet<Domain.Booking.Booking> Bookings => Set<Domain.Booking.Booking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Guid>().HaveConversion<GuidBinaryConverter>().HaveColumnType("binary(16)");
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime(6)");
        // Oracle's MySQL driver returns DATE columns as DateTime and can't read them into DateOnly directly.
        b.Properties<DateOnly>().HaveConversion<DateOnlyConverter>().HaveColumnType("date");
        // Times of day (weekly hours) as minutes since midnight: portable and easy to compare in SQL.
        b.Properties<TimeOnly>().HaveConversion<TimeOnlyMinutesConverter>().HaveColumnType("int");
        b.Properties<string>().HaveMaxLength(255);
        b.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Identity (users are platform-level, not tenant-owned)
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PasswordHash).HasMaxLength(500);
            e.Ignore(u => u.FullName);
            e.Ignore(u => u.Initials);
        });
        b.Entity<IdentityUserClaim<Guid>>(e =>
        {
            e.ToTable("user_claims");
            e.Property(c => c.ClaimValue).HasMaxLength(1000);
        });
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<Guid>>(e =>
        {
            e.ToTable("user_tokens");
            e.Property(t => t.Value).HasMaxLength(2000);
        });

        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Slug).HasMaxLength(60);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.TimeZone).HasMaxLength(64);
            e.Property(t => t.PostalAddress).HasMaxLength(500);
            e.Property(t => t.EstimateTerms).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(t => t.InsuranceEstimateTerms).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(t => t.DefaultTaxRate).HasPrecision(6, 3);
            e.Property(t => t.InsuranceOverheadPct).HasPrecision(5, 2);
            e.Property(t => t.InsuranceProfitPct).HasPrecision(5, 2);
        });

        b.Entity<TenantMembership>(e =>
        {
            e.ToTable("tenant_memberships");
            e.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(m => m.Roles).WithOne().HasForeignKey(r => r.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.Title).HasMaxLength(100);
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.Property(p => p.PermissionKey).HasMaxLength(100);
            e.HasIndex(p => new { p.RoleId, p.PermissionKey }).IsUnique();
            e.HasIndex(p => p.TenantId);
        });

        b.Entity<MembershipRole>(e =>
        {
            e.ToTable("membership_roles");
            e.HasIndex(r => new { r.MembershipId, r.RoleId }).IsUnique();
            e.HasIndex(r => r.TenantId);
            e.HasOne(r => r.Role).WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.EntityType).HasMaxLength(100);
            e.Property(a => a.Summary).HasMaxLength(1000);
            e.Property(a => a.DetailsJson).HasColumnType("json").Metadata.SetMaxLength(null);
            e.Property(a => a.IpAddress).HasMaxLength(45);
            e.HasIndex(a => new { a.TenantId, a.CreatedAt });
            e.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        });

        b.Entity<TenantSequence>(e =>
        {
            e.ToTable("tenant_sequences");
            e.HasKey(s => new { s.TenantId, s.Key, s.Year });
            e.Property(s => s.Key).HasMaxLength(50);
        });

        ConfigureCrm(b);
        ConfigureInsurance(b);
        ConfigureMeasurements(b);
        ConfigureEstimating(b);
        ConfigureProduction(b);
        ConfigureSubcontractors(b);
        ConfigureWork(b);
        ConfigureCommunication(b);
        ConfigureBooking(b);
        ApplyTenantFilters(b);
    }

    private static void ConfigureWork(ModelBuilder b)
    {
        b.Entity<Domain.Work.TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.Property(t => t.Title).HasMaxLength(300);
            e.Property(t => t.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(t => t.Priority).HasMaxLength(10);
            e.Property(t => t.EntityType).HasMaxLength(30);
            e.Ignore(t => t.IsOpen);
            e.HasIndex(t => new { t.TenantId, t.AssignedToUserId, t.CompletedAt, t.DueAt });
            e.HasIndex(t => new { t.TenantId, t.EntityType, t.EntityId });
        });

        b.Entity<Domain.Work.AppointmentAttendee>(e =>
        {
            e.ToTable("appointment_attendees");
            e.HasIndex(a => new { a.TenantId, a.AppointmentId, a.UserId }).IsUnique();
            e.HasIndex(a => new { a.TenantId, a.UserId });
            e.HasOne<Domain.Work.Appointment>().WithMany().HasForeignKey(a => a.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Domain.Work.Appointment>(e =>
        {
            e.ToTable("appointments");
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.TypeKey).HasMaxLength(60);
            e.Property(a => a.Location).HasMaxLength(300);
            e.Property(a => a.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(a => a.Status).HasMaxLength(20);
            e.Property(a => a.EntityType).HasMaxLength(30);
            e.HasIndex(a => new { a.TenantId, a.StartsAt });
            e.HasIndex(a => new { a.TenantId, a.AssignedToUserId, a.StartsAt });
            e.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        });

        b.Entity<Domain.Work.Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(n => n.Kind).HasMaxLength(60);
            e.Property(n => n.Title).HasMaxLength(300);
            e.Property(n => n.Body).HasMaxLength(1000);
            e.Property(n => n.Url).HasMaxLength(500);
            e.HasIndex(n => new { n.TenantId, n.UserId, n.ReadAt, n.CreatedAt });
            e.Property(n => n.DedupeKey).HasMaxLength(200);
            e.HasIndex(n => new { n.TenantId, n.UserId, n.DedupeKey }).IsUnique(); // NULLs (ordinary notifications) never collide
        });

        b.Entity<Domain.Work.AutomationRule>(e =>
        {
            e.ToTable("automation_rules");
            e.Property(r => r.Name).HasMaxLength(150);
            e.Property(r => r.Trigger).HasMaxLength(30);
            e.Property(r => r.Action).HasMaxLength(30);
            e.Property(r => r.Text).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.Trigger, r.StageId });
        });

        b.Entity<Domain.Work.AutomationRun>(e =>
        {
            e.ToTable("automation_runs");
            e.Property(r => r.EntityType).HasMaxLength(30);
            e.Property(r => r.Status).HasMaxLength(20);
            e.Property(r => r.Detail).HasMaxLength(500);
            e.HasIndex(r => new { r.RuleId, r.StageChangeId }).IsUnique();
            e.HasIndex(r => new { r.TenantId, r.CreatedAt });
        });

        b.Entity<Domain.Ai.AiConversation>(e =>
        {
            e.ToTable("ai_conversations");
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.EntityType).HasMaxLength(30);
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.CostUsd).HasPrecision(12, 6);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.LastMessageAt });
        });

        b.Entity<Domain.Ai.AiChatMessage>(e =>
        {
            e.ToTable("ai_messages");
            e.Property(x => x.Role).HasMaxLength(10);
            e.Property(x => x.BlocksJson).HasColumnType("mediumtext").Metadata.SetMaxLength(null);
            e.Property(x => x.Text).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(x => new { x.ConversationId, x.Seq }).IsUnique();
        });

        b.Entity<Domain.Ai.AiToolCall>(e =>
        {
            e.ToTable("ai_tool_calls");
            e.Property(x => x.Tool).HasMaxLength(60);
            e.Property(x => x.InputJson).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(x => x.OutputSummary).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.ConversationId });
        });

        b.Entity<Domain.Ai.AiUsageDaily>(e =>
        {
            e.ToTable("ai_usage_daily");
            e.Property(x => x.CostUsd).HasPrecision(12, 6);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Date }).IsUnique();
        });

        b.Entity<ScopeImport>(e =>
        {
            e.ToTable("scope_imports");
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Carrier).HasMaxLength(150);
            e.Property(x => x.CarrierPriceList).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.ExtractedJson).HasColumnType("mediumtext").Metadata.SetMaxLength(null);
            e.Property(x => x.LinesJson).HasColumnType("mediumtext").Metadata.SetMaxLength(null);
            e.HasIndex(x => new { x.TenantId, x.EstimateId });
        });
    }

    private static void ConfigureBooking(ModelBuilder b)
    {
        b.Entity<Domain.Booking.BookingType>(e =>
        {
            e.ToTable("booking_types");
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.AppointmentTypeKey).HasMaxLength(60);
            e.HasMany(x => x.Hosts).WithOne().HasForeignKey(h => h.BookingTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        });

        b.Entity<Domain.Booking.BookingTypeHost>(e =>
        {
            e.ToTable("booking_type_hosts");
            e.HasIndex(x => new { x.BookingTypeId, x.UserId }).IsUnique();
        });

        b.Entity<Domain.Booking.BusyCalendar>(e =>
        {
            e.ToTable("busy_calendars");
            e.Property(c => c.Label).HasMaxLength(100);
            e.Property(c => c.UrlProtected).HasMaxLength(4000);
            e.Property(c => c.Host).HasMaxLength(255);
            e.Property(c => c.LastError).HasMaxLength(300);
            e.HasIndex(c => new { c.TenantId, c.UserId });
        });

        b.Entity<Domain.Booking.BusyBlock>(e =>
        {
            e.ToTable("busy_blocks");
            e.HasIndex(x => new { x.TenantId, x.UserId, x.StartsAt });
            e.HasIndex(x => new { x.TenantId, x.BusyCalendarId });
            e.HasOne<Domain.Booking.BusyCalendar>().WithMany().HasForeignKey(x => x.BusyCalendarId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Domain.Booking.AvailabilityRule>(e =>
        {
            e.ToTable("availability_rules");
            e.HasIndex(x => new { x.TenantId, x.UserId, x.DayOfWeek });
        });

        b.Entity<Domain.Booking.Booking>(e =>
        {
            e.ToTable("bookings");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ReviewStatus).HasMaxLength(20);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Line1).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.State).HasMaxLength(30);
            e.Property(x => x.PostalCode).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.SourceIp).HasMaxLength(64);
            e.Property(x => x.SlotKey).HasMaxLength(80);
            e.HasIndex(x => x.SlotKey).IsUnique(); // NULLs (cancelled) don't collide in MySQL
            e.HasIndex(x => new { x.TenantId, x.ReviewStatus, x.StartsAt });
            e.HasIndex(x => new { x.TenantId, x.HostUserId, x.StartsAt });
        });
    }

    private static void ConfigureCommunication(ModelBuilder b)
    {
        b.Entity<Domain.Communication.MessageTemplate>(e =>
        {
            e.ToTable("message_templates");
            e.Property(t => t.Channel).HasMaxLength(10);
            e.Property(t => t.Name).HasMaxLength(150);
            e.Property(t => t.Subject).HasMaxLength(300);
            e.Property(t => t.Body).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(t => t.Category).HasMaxLength(20);
            e.HasIndex(t => new { t.TenantId, t.Channel, t.Name }).IsUnique();
        });

        b.Entity<Domain.Communication.EmailMessage>(e =>
        {
            e.ToTable("email_messages");
            e.Property(m => m.Direction).HasMaxLength(10);
            e.Property(m => m.FromAddress).HasMaxLength(320);
            e.Property(m => m.Subject).HasMaxLength(300);
            e.Property(m => m.Body).HasColumnType("mediumtext").Metadata.SetMaxLength(null);
            e.Property(m => m.Status).HasMaxLength(20);
            e.Property(m => m.ProviderMessageId).HasMaxLength(300);
            e.Property(m => m.Error).HasMaxLength(1000);
            e.Property(m => m.EntityType).HasMaxLength(30);
            e.HasMany(m => m.Recipients).WithOne().HasForeignKey(r => r.EmailMessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.TenantId, m.SentAt });
        });

        b.Entity<Domain.Communication.EmailRecipient>(e =>
        {
            e.ToTable("email_recipients");
            e.Property(r => r.Kind).HasMaxLength(5);
            e.Property(r => r.Address).HasMaxLength(320);
            e.HasIndex(r => new { r.TenantId, r.Address });
            e.HasIndex(r => new { r.TenantId, r.ContactId });
        });

        b.Entity<Domain.Communication.SmsMessage>(e =>
        {
            e.ToTable("sms_messages");
            e.Property(m => m.Direction).HasMaxLength(10);
            e.Property(m => m.FromNumber).HasMaxLength(20);
            e.Property(m => m.ToNumber).HasMaxLength(20);
            e.Property(m => m.Body).HasMaxLength(1600);
            e.Property(m => m.Status).HasMaxLength(20);
            e.Property(m => m.ProviderSid).HasMaxLength(100);
            e.Property(m => m.Error).HasMaxLength(1000);
            e.Property(m => m.EntityType).HasMaxLength(30);
            e.HasIndex(m => new { m.TenantId, m.FromNumber, m.ContactId });
            e.HasIndex(m => new { m.TenantId, m.ContactId, m.At });
            e.HasIndex(m => new { m.TenantId, m.FromNumber, m.At });
        });

        b.Entity<Domain.Communication.ContactConsent>(e =>
        {
            e.ToTable("contact_consents");
            e.Property(c => c.Channel).HasMaxLength(10);
            e.Property(c => c.Purpose).HasMaxLength(20);
            e.Property(c => c.Status).HasMaxLength(20);
            e.Property(c => c.Source).HasMaxLength(30);
            e.Property(c => c.Evidence).HasMaxLength(1000);
            e.HasIndex(c => new { c.TenantId, c.ContactId, c.Channel, c.Purpose, c.At });
        });

        b.Entity<Domain.Communication.Suppression>(e =>
        {
            e.ToTable("suppressions");
            e.Property(s => s.Channel).HasMaxLength(10);
            e.Property(s => s.Address).HasMaxLength(320);
            e.Property(s => s.Scope).HasMaxLength(20);
            e.Property(s => s.Reason).HasMaxLength(20);
            e.HasIndex(s => new { s.TenantId, s.Channel, s.Address });
        });

        b.Entity<Domain.Communication.TenantPhoneNumber>(e =>
        {
            e.ToTable("tenant_phone_numbers");
            e.Property(p => p.Number).HasMaxLength(20);
            e.Property(p => p.Provider).HasMaxLength(20);
            e.HasIndex(p => p.Number).IsUnique(); // one tenant per number, across the platform
        });

        b.Entity<Domain.Communication.Blast>(e =>
        {
            e.ToTable("blasts");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.Body).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(x => x.AudienceJson).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.ScheduledAt }); // the sender looks across tenants
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        b.Entity<Domain.Communication.BlastRecipient>(e =>
        {
            e.ToTable("blast_recipients");
            e.Property(x => x.Address).HasMaxLength(320);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.BlastId, x.Status });
            e.HasIndex(x => new { x.BlastId, x.ContactId }).IsUnique();
        });

        b.Entity<Domain.Communication.Sequence>(e =>
        {
            e.ToTable("sequences");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Category).HasMaxLength(20);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.SequenceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        b.Entity<Domain.Communication.SequenceStep>(e =>
        {
            e.ToTable("sequence_steps");
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.Body).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(x => new { x.SequenceId, x.Sort });
        });

        b.Entity<Domain.Communication.Enrollment>(e =>
        {
            e.ToTable("enrollments");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.StopReason).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.NextRunAt }); // the runner looks across tenants
            e.HasIndex(x => new { x.TenantId, x.ContactId, x.SequenceId });
            e.HasIndex(x => new { x.TenantId, x.OpportunityId });
        });

        b.Entity<Domain.Communication.EnrollmentStepRun>(e =>
        {
            e.ToTable("enrollment_step_runs");
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.HasIndex(x => new { x.EnrollmentId, x.CreatedAt });
        });
    }

    private static void ConfigureSubcontractors(ModelBuilder b)
    {
        b.Entity<Subcontractor>(e =>
        {
            e.ToTable("subcontractors");
            e.Property(s => s.TradeCodes).HasMaxLength(500);
            e.Property(s => s.Status).HasMaxLength(20);
            e.Property(s => s.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Ignore(s => s.Trades);
            e.HasIndex(s => new { s.TenantId, s.ContactId }).IsUnique();
            e.HasOne<Contact>().WithMany().HasForeignKey(s => s.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SubcontractorDocument>(e =>
        {
            e.ToTable("subcontractor_documents");
            e.Property(d => d.DocTypeKey).HasMaxLength(40);
            e.HasIndex(d => new { d.TenantId, d.SubcontractorId });
            e.HasOne<Subcontractor>().WithMany().HasForeignKey(d => d.SubcontractorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Document>().WithMany().HasForeignKey(d => d.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SubcontractorRate>(e =>
        {
            e.ToTable("subcontractor_rates");
            e.Property(r => r.Description).HasMaxLength(200);
            e.Property(r => r.Unit).HasMaxLength(10);
            e.Property(r => r.Rate).HasPrecision(12, 4);
            e.HasIndex(r => new { r.TenantId, r.SubcontractorId });
            e.HasOne<Subcontractor>().WithMany().HasForeignKey(r => r.SubcontractorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkOrder>(e =>
        {
            e.ToTable("work_orders");
            e.Property(w => w.Number).HasMaxLength(30);
            e.Property(w => w.Status).HasMaxLength(20);
            e.Property(w => w.AcceptStatus).HasMaxLength(20);
            e.Property(w => w.AcceptedName).HasMaxLength(150);
            e.Property(w => w.DeclineReason).HasMaxLength(500);
            e.Property(w => w.Amount).HasPrecision(12, 2);
            e.Property(w => w.ArrivalTime).HasMaxLength(50);
            e.Property(w => w.Instructions).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(w => w.CompletionNote).HasMaxLength(2000);
            e.HasIndex(w => new { w.TenantId, w.Number }).IsUnique();
            e.HasIndex(w => new { w.TenantId, w.SubcontractorId });
            e.HasIndex(w => new { w.TenantId, w.ProjectTradeId });
            e.HasOne<Project>().WithMany().HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectTrade>().WithMany().HasForeignKey(w => w.ProjectTradeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Subcontractor>().WithMany().HasForeignKey(w => w.SubcontractorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkOrderLine>(e =>
        {
            e.ToTable("work_order_lines");
            e.Property(l => l.Description).HasMaxLength(300);
            e.Property(l => l.Qty).HasPrecision(14, 4);
            e.Property(l => l.Unit).HasMaxLength(10);
            e.Property(l => l.Rate).HasPrecision(12, 4);
            e.Ignore(l => l.Amount);
            e.HasIndex(l => new { l.TenantId, l.WorkOrderId });
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExternalAccessToken>(e =>
        {
            e.ToTable("external_access_tokens");
            e.Property(t => t.Purpose).HasMaxLength(30);
            e.Property(t => t.EntityType).HasMaxLength(30);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.TenantId, t.EntityType, t.EntityId });
        });
    }

    private static void ConfigureProduction(ModelBuilder b)
    {
        b.Entity<WorkflowStage>(e =>
        {
            e.ToTable("workflow_stages");
            e.Property(s => s.Level).HasMaxLength(20);
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Role).HasMaxLength(30);
            e.Property(s => s.Color).HasMaxLength(20);
            e.HasIndex(s => new { s.TenantId, s.Level, s.Sort });
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.Property(p => p.Number).HasMaxLength(30);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.OriginalContract).HasPrecision(12, 2);
            e.Property(p => p.ApprovedChangeOrders).HasPrecision(12, 2);
            e.Property(p => p.RemovalReasonKey).HasMaxLength(60);
            e.Property(p => p.RemovalNote).HasMaxLength(500);
            e.Property(p => p.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Ignore(p => p.ContractValue);
            e.HasIndex(p => new { p.TenantId, p.Number }).IsUnique();
            e.HasIndex(p => new { p.TenantId, p.StageId });
            e.HasIndex(p => new { p.TenantId, p.OpportunityId });
            e.HasIndex(p => new { p.TenantId, p.EstimateId });
            e.HasOne<Opportunity>().WithMany().HasForeignKey(p => p.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(p => p.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Estimate>().WithMany().HasForeignKey(p => p.EstimateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstimateVersion>().WithMany().HasForeignKey(p => p.EstimateVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkflowStage>().WithMany().HasForeignKey(p => p.StageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderSheetLine>(e =>
        {
            e.ToTable("order_sheet_lines");
            e.Property(l => l.Source).HasMaxLength(20);
            e.Property(l => l.Sku).HasMaxLength(60);
            e.Property(l => l.Description).HasMaxLength(500);
            e.Property(l => l.Qty).HasPrecision(12, 3);
            e.Property(l => l.Unit).HasMaxLength(20);
            e.Property(l => l.Notes).HasMaxLength(300);
            e.Property(l => l.UnitCost).HasPrecision(12, 4);
            e.HasIndex(l => new { l.TenantId, l.ProjectTradeId, l.Sort });
            e.HasOne<ProjectTrade>().WithMany().HasForeignKey(l => l.ProjectTradeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProjectTrade>(e =>
        {
            e.ToTable("project_trades");
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.Amount).HasPrecision(12, 2);
            e.Property(t => t.CrewNote).HasMaxLength(500);
            e.Property(t => t.RemovalReasonKey).HasMaxLength(60);
            e.Property(t => t.RemovalNote).HasMaxLength(500);
            e.Property(t => t.OrderTakeoffJson).HasMaxLength(2000);
            e.Property(t => t.OrderSupplier).HasMaxLength(150);
            e.Property(t => t.OrderNotes).HasMaxLength(1000);
            e.HasIndex(t => new { t.TenantId, t.ProjectId });
            e.HasIndex(t => new { t.TenantId, t.StageId });
            e.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TradeType>().WithMany().HasForeignKey(t => t.TradeTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkflowStage>().WithMany().HasForeignKey(t => t.StageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkflowStageChange>(e =>
        {
            e.ToTable("workflow_stage_changes");
            e.Property(c => c.EntityType).HasMaxLength(20);
            e.Property(c => c.Note).HasMaxLength(500);
            e.HasIndex(c => new { c.TenantId, c.EntityType, c.EntityId });
        });

        b.Entity<ChangeOrder>(e =>
        {
            e.ToTable("change_orders");
            e.Property(c => c.Description).HasMaxLength(1000);
            e.Property(c => c.Reason).HasMaxLength(500);
            e.Property(c => c.Amount).HasPrecision(12, 2);
            e.Property(c => c.Status).HasMaxLength(20);
            e.Property(c => c.ApprovedByName).HasMaxLength(150);
            e.HasIndex(c => new { c.TenantId, c.ProjectId, c.Sequence }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ChangeOrderLine>(e =>
        {
            e.ToTable("change_order_lines");
            e.Property(l => l.Kind).HasMaxLength(20);
            e.Property(l => l.Code).HasMaxLength(60);
            e.Property(l => l.Description).HasMaxLength(1000);
            e.Property(l => l.Unit).HasMaxLength(20);
            e.Property(l => l.Qty).HasPrecision(12, 3);
            e.Property(l => l.UnitPrice).HasPrecision(12, 2);
            e.Property(l => l.Amount).HasPrecision(12, 2);
            e.HasIndex(l => new { l.TenantId, l.ChangeOrderId, l.Sort });
            e.HasOne<ChangeOrder>().WithMany().HasForeignKey(l => l.ChangeOrderId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEstimating(ModelBuilder b)
    {
        b.Entity<CatalogCategory>(e =>
        {
            e.ToTable("catalog_categories");
            e.Property(c => c.Code).HasMaxLength(20);
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        });

        b.Entity<CatalogItem>(e =>
        {
            e.ToTable("catalog_items");
            e.Property(c => c.Code).HasMaxLength(40);
            e.Property(c => c.Description).HasMaxLength(500);
            e.Property(c => c.Unit).HasMaxLength(10);
            e.Property(c => c.CoverageNotes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(c => new { c.TenantId, c.CategoryId, c.Code }).IsUnique();
            e.HasIndex(c => new { c.TenantId, c.Code });
            e.HasOne<CatalogCategory>().WithMany().HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PriceList>(e =>
        {
            e.ToTable("price_lists");
            e.Property(p => p.Name).HasMaxLength(150);
            e.Property(p => p.RegionCode).HasMaxLength(30);
            e.Property(p => p.Source).HasMaxLength(20);
            e.Property(p => p.Status).HasMaxLength(20);
            e.Property(p => p.Notes).HasMaxLength(2000);
        });

        b.Entity<PriceListItem>(e =>
        {
            e.ToTable("price_list_items");
            e.Property(p => p.Activity).HasMaxLength(20);
            e.Property(p => p.UnitPrice).HasPrecision(12, 4);
            e.Property(p => p.MaterialPrice).HasPrecision(12, 4);
            e.Property(p => p.LaborPrice).HasPrecision(12, 4);
            e.HasIndex(p => new { p.TenantId, p.PriceListId, p.CatalogItemId, p.Activity }).IsUnique();
            e.HasOne<PriceList>().WithMany().HasForeignKey(p => p.PriceListId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CatalogItem>().WithMany().HasForeignKey(p => p.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(p => p.Sku).HasMaxLength(60);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Unit).HasMaxLength(10);
            e.Property(p => p.DefaultCost).HasPrecision(12, 4);
            e.Property(p => p.DefaultPrice).HasPrecision(12, 4);
            e.Property(p => p.Supplier).HasMaxLength(100);
            e.Property(p => p.TakeoffSlot).HasMaxLength(50);
            e.Property(p => p.TakeoffMultiplier).HasPrecision(14, 6);
            e.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
        });

        b.Entity<TakeoffRule>(e =>
        {
            e.ToTable("takeoff_rules");
            e.Property(r => r.Name).HasMaxLength(150);
            e.Property(r => r.Slot).HasMaxLength(50);
            e.Property(r => r.Driver).HasMaxLength(60);
            e.Property(r => r.Multiplier).HasPrecision(14, 6);
            e.Property(r => r.WastePercent).HasPrecision(6, 3);
            e.Property(r => r.RoundMode).HasMaxLength(10);
            e.Property(r => r.RoundTo).HasPrecision(10, 4);
            e.Property(r => r.MinQty).HasPrecision(12, 3);
            e.Property(r => r.MaxQty).HasPrecision(12, 3);
            e.Property(r => r.Cond1Field).HasMaxLength(60);
            e.Property(r => r.Cond1Op).HasMaxLength(5);
            e.Property(r => r.Cond1Value).HasPrecision(12, 3);
            e.Property(r => r.Cond2Field).HasMaxLength(60);
            e.Property(r => r.Cond2Op).HasMaxLength(5);
            e.Property(r => r.Cond2Value).HasPrecision(12, 3);
            e.Property(r => r.OptionKey).HasMaxLength(50);
            e.Property(r => r.OptionLabel).HasMaxLength(150);
            e.Property(r => r.Description).HasMaxLength(500);
            e.Property(r => r.ScopeText).HasMaxLength(1000);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.TradeTypeId, r.Sort });
            e.HasOne<Domain.Measurements.TradeType>().WithMany().HasForeignKey(r => r.TradeTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Estimate>(e =>
        {
            e.ToTable("estimates");
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Method).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TaxBasis).HasMaxLength(20);
            foreach (var p in new[] { nameof(Estimate.OverheadPct), nameof(Estimate.ProfitPct), nameof(Estimate.TaxRate) })
                e.Property(p).HasPrecision(7, 4);
            foreach (var p in new[] { nameof(Estimate.Subtotal), nameof(Estimate.Overhead), nameof(Estimate.Profit), nameof(Estimate.Tax), nameof(Estimate.Total) })
                e.Property(p).HasPrecision(12, 2);
            e.Property(x => x.Terms).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(x => x.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.OpportunityId });
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<InsuranceClaim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PriceList>().WithMany().HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EstimateSection>(e =>
        {
            e.ToTable("estimate_sections");
            e.Property(s => s.Name).HasMaxLength(150);
            e.Property(s => s.ScopeOfWork).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(s => s.TakeoffJson).HasMaxLength(2000);
            e.Property(s => s.TakeoffScope).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(s => new { s.TenantId, s.EstimateId });
            e.HasOne<Estimate>().WithMany().HasForeignKey(s => s.EstimateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MeasurementSet>().WithMany().HasForeignKey(s => s.MeasurementSetId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EstimateLine>(e =>
        {
            e.ToTable("estimate_lines");
            e.Property(l => l.Kind).HasMaxLength(20);
            e.Property(l => l.Activity).HasMaxLength(20);
            e.Property(l => l.Code).HasMaxLength(60);
            e.Property(l => l.Description).HasMaxLength(500);
            e.Property(l => l.Qty).HasPrecision(14, 4);
            e.Property(l => l.Unit).HasMaxLength(10);
            e.Property(l => l.UnitPrice).HasPrecision(12, 4);
            e.Property(l => l.UnitCost).HasPrecision(12, 4);
            e.Property(l => l.DefaultUnitPrice).HasPrecision(12, 4);
            e.Property(l => l.Notes).HasMaxLength(1000);
            e.Property(l => l.ApprovalStatus).HasMaxLength(20);
            e.Property(l => l.ApprovalReason).HasMaxLength(200);
            e.Property(l => l.ApprovalNote).HasMaxLength(500);
            e.HasIndex(l => new { l.TenantId, l.EstimateId });
            e.HasIndex(l => new { l.TenantId, l.ApprovalStatus });
            e.HasOne<EstimateSection>().WithMany().HasForeignKey(l => l.SectionId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(l => l.Amount);
        });

        b.Entity<EstimateVersion>(e =>
        {
            e.ToTable("estimate_versions");
            e.Property(v => v.Status).HasMaxLength(20);
            e.Property(v => v.RevisionReason).HasMaxLength(500);
            foreach (var p in new[] { nameof(EstimateVersion.Subtotal), nameof(EstimateVersion.Overhead), nameof(EstimateVersion.Profit), nameof(EstimateVersion.Tax), nameof(EstimateVersion.Total) })
                e.Property(p).HasPrecision(12, 2);
            e.Property(v => v.ContentJson).HasColumnType("json").Metadata.SetMaxLength(null);
            e.Property(v => v.ContentHash).HasMaxLength(64);
            e.Property(v => v.SignerName).HasMaxLength(150);
            e.HasIndex(v => new { v.TenantId, v.EstimateId, v.VersionNo }).IsUnique();
            e.HasOne<Estimate>().WithMany().HasForeignKey(v => v.EstimateId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureMeasurements(ModelBuilder b)
    {
        b.Entity<TradeType>(e =>
        {
            e.ToTable("trade_types");
            e.Property(t => t.Name).HasMaxLength(100);
            e.Property(t => t.Code).HasMaxLength(50);
            e.Property(t => t.MeasurementMode).HasMaxLength(20);
            e.Property(t => t.Color).HasMaxLength(20);
            e.Property(t => t.Icon).HasMaxLength(50);
            e.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        });

        b.Entity<MeasurementFieldDef>(e =>
        {
            e.ToTable("measurement_field_defs");
            e.Property(f => f.Key).HasMaxLength(60);
            e.Property(f => f.Label).HasMaxLength(100);
            e.Property(f => f.Unit).HasMaxLength(20);
            e.Property(f => f.Group).HasMaxLength(60);
            e.HasIndex(f => new { f.TenantId, f.TradeTypeId, f.Key }).IsUnique();
            e.HasOne<TradeType>().WithMany().HasForeignKey(f => f.TradeTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MeasurementSet>(e =>
        {
            e.ToTable("measurement_sets");
            e.Property(s => s.Source).HasMaxLength(30);
            e.Property(s => s.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(s => s.ExtractionJson).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(s => new { s.TenantId, s.PropertyId });
            e.HasIndex(s => new { s.TenantId, s.StructureId, s.TradeTypeId, s.VersionNo }).IsUnique();
            e.HasOne<Property>().WithMany().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PropertyStructure>().WithMany().HasForeignKey(s => s.StructureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TradeType>().WithMany().HasForeignKey(s => s.TradeTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Document>().WithMany().HasForeignKey(s => s.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.Values).WithOne().HasForeignKey(v => v.SetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MeasurementValue>(e =>
        {
            e.ToTable("measurement_values");
            e.Property(v => v.SurfaceKey).HasMaxLength(30);
            e.Property(v => v.FieldKey).HasMaxLength(60);
            e.Property(v => v.Value).HasPrecision(14, 4);
            e.HasIndex(v => new { v.SetId, v.SurfaceKey, v.FieldKey }).IsUnique();
        });
    }

    private static void ConfigureInsurance(ModelBuilder b)
    {
        b.Entity<Carrier>(e =>
        {
            e.ToTable("carriers");
            e.Property(c => c.Name).HasMaxLength(150);
            e.Property(c => c.Phone).HasMaxLength(40);
            e.Property(c => c.ClaimsEmail).HasMaxLength(254);
            e.Property(c => c.ClaimsFax).HasMaxLength(40);
            e.Property(c => c.Website).HasMaxLength(200);
            e.Property(c => c.PreferredSubmission).HasMaxLength(30);
            e.Property(c => c.Address).HasMaxLength(300);
            e.HasIndex(c => c.Name).IsUnique();
            e.HasData(PlatformCarriers.Seed());
        });

        b.Entity<TenantCarrier>(e =>
        {
            e.ToTable("tenant_carriers");
            e.Property(c => c.Name).HasMaxLength(150);
            e.Property(c => c.Phone).HasMaxLength(40);
            e.Property(c => c.ClaimsEmail).HasMaxLength(254);
            e.Property(c => c.ClaimsFax).HasMaxLength(40);
            e.Property(c => c.Website).HasMaxLength(200);
            e.Property(c => c.PreferredSubmission).HasMaxLength(30);
            e.Property(c => c.Address).HasMaxLength(300);
            e.Property(c => c.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(c => new { c.TenantId, c.CarrierId });
            e.HasOne<Carrier>().WithMany().HasForeignKey(c => c.CarrierId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StormEvent>(e =>
        {
            e.ToTable("storm_events");
            e.Property(s => s.Name).HasMaxLength(150);
            e.Property(s => s.PerilKey).HasMaxLength(60);
            e.Property(s => s.MaxHailSizeIn).HasPrecision(4, 2);
            e.Property(s => s.ZipCodes).HasMaxLength(2000);
            e.Property(s => s.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(s => new { s.TenantId, s.EventDate });
            e.Ignore(s => s.Zips);
        });

        b.Entity<InsuranceClaim>(e =>
        {
            e.ToTable("insurance_claims");
            e.Property(c => c.ClaimNumber).HasMaxLength(60);
            e.Property(c => c.PolicyNumber).HasMaxLength(60);
            e.Property(c => c.PerilKey).HasMaxLength(60);
            e.Property(c => c.Status).HasMaxLength(20);
            e.Property(c => c.Deductible).HasPrecision(12, 2);
            e.Property(c => c.ApprovedAmount).HasPrecision(12, 2);
            e.Property(c => c.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(c => new { c.TenantId, c.OpportunityId });
            e.HasIndex(c => new { c.TenantId, c.PropertyId });
            e.HasIndex(c => new { c.TenantId, c.ClaimNumber });
            e.HasIndex(c => new { c.TenantId, c.StormEventId });
            e.HasOne<Opportunity>().WithMany().HasForeignKey(c => c.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(c => c.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TenantCarrier>().WithMany().HasForeignKey(c => c.TenantCarrierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StormEvent>().WithMany().HasForeignKey(c => c.StormEventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(c => c.AdjusterContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(c => c.DeskAdjusterContactId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Supplement>(e =>
        {
            e.ToTable("supplements");
            e.Property(s => s.Status).HasMaxLength(20);
            e.Property(s => s.Reason).HasMaxLength(500);
            e.Property(s => s.RequestedAmount).HasPrecision(12, 2);
            e.Property(s => s.ApprovedAmount).HasPrecision(12, 2);
            e.Property(s => s.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(s => new { s.TenantId, s.ClaimId });
            e.HasOne<InsuranceClaim>().WithMany().HasForeignKey(s => s.ClaimId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Opportunity>().HasOne<StormEvent>().WithMany().HasForeignKey(o => o.StormEventId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCrm(ModelBuilder b)
    {
        b.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.Property(c => c.Type).HasMaxLength(20);
            e.Property(c => c.FirstName).HasMaxLength(100);
            e.Property(c => c.LastName).HasMaxLength(100);
            e.Property(c => c.OrganizationName).HasMaxLength(200);
            e.Property(c => c.DisplayName).HasMaxLength(200);
            e.Property(c => c.Status).HasMaxLength(20);
            e.Property(c => c.SourceKey).HasMaxLength(60);
            e.Property(c => c.CautionNote).HasMaxLength(500);
            e.Property(c => c.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(c => new { c.TenantId, c.DisplayName });
            e.HasIndex(c => new { c.TenantId, c.LastName, c.FirstName });
            e.HasIndex(c => new { c.TenantId, c.OwnerUserId });
            e.HasMany(c => c.Methods).WithOne().HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(c => c.IsOrganization);
        });

        b.Entity<ContactMethod>(e =>
        {
            e.ToTable("contact_methods");
            e.Property(m => m.Kind).HasMaxLength(20);
            e.Property(m => m.Value).HasMaxLength(254);
            e.Property(m => m.NormalizedValue).HasMaxLength(254);
            e.Property(m => m.Label).HasMaxLength(50);
            e.HasIndex(m => new { m.TenantId, m.NormalizedValue });
        });

        b.Entity<ContactAddress>(e =>
        {
            e.ToTable("contact_addresses");
            e.Property(a => a.Kind).HasMaxLength(20);
            e.Property(a => a.Line1).HasMaxLength(200);
            e.Property(a => a.Line2).HasMaxLength(100);
            e.Property(a => a.City).HasMaxLength(100);
            e.Property(a => a.State).HasMaxLength(50);
            e.Property(a => a.PostalCode).HasMaxLength(20);
            e.Ignore(a => a.SingleLine);
            e.HasIndex(a => new { a.TenantId, a.ContactId, a.Kind }).IsUnique();
            e.HasOne<Contact>().WithMany().HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ContactRelationship>(e =>
        {
            e.ToTable("contact_relationships");
            e.Property(r => r.Title).HasMaxLength(100);
            e.HasIndex(r => new { r.TenantId, r.PersonContactId });
            e.HasIndex(r => new { r.TenantId, r.OrganizationContactId });
            e.HasOne<Contact>().WithMany().HasForeignKey(r => r.PersonContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(r => r.OrganizationContactId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Property>(e =>
        {
            e.ToTable("properties");
            e.Property(p => p.Line1).HasMaxLength(200);
            e.Property(p => p.Line2).HasMaxLength(100);
            e.Property(p => p.City).HasMaxLength(100);
            e.Property(p => p.State).HasMaxLength(50);
            e.Property(p => p.PostalCode).HasMaxLength(20);
            e.Property(p => p.Country).HasMaxLength(2);
            e.Property(p => p.NormalizedKey).HasMaxLength(320);
            e.Property(p => p.Kind).HasMaxLength(20);
            e.Property(p => p.Latitude).HasPrecision(9, 6);
            e.Property(p => p.Longitude).HasPrecision(9, 6);
            e.Property(p => p.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(p => new { p.TenantId, p.NormalizedKey }).IsUnique();
            e.HasIndex(p => new { p.TenantId, p.PostalCode });
            e.HasMany(p => p.Structures).WithOne().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(p => p.SingleLine);
            e.Ignore(p => p.CityLine);
        });

        b.Entity<PropertyContact>(e =>
        {
            e.ToTable("property_contacts");
            e.Property(pc => pc.RoleKey).HasMaxLength(60);
            e.HasIndex(pc => new { pc.TenantId, pc.PropertyId });
            e.HasIndex(pc => new { pc.TenantId, pc.ContactId });
            e.HasOne<Property>().WithMany().HasForeignKey(pc => pc.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(pc => pc.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(pc => pc.IsCurrent);
        });

        b.Entity<PropertyStructure>(e =>
        {
            e.ToTable("property_structures");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Kind).HasMaxLength(50);
        });

        b.Entity<PipelineStage>(e =>
        {
            e.ToTable("pipeline_stages");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Color).HasMaxLength(20);
            e.Property(s => s.Outcome).HasMaxLength(20);
            e.HasIndex(s => new { s.TenantId, s.Sort });
        });

        b.Entity<Opportunity>(e =>
        {
            e.ToTable("opportunities");
            e.Property(o => o.Number).HasMaxLength(30);
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.SourceKey).HasMaxLength(60);
            e.Property(o => o.Outcome).HasMaxLength(20);
            e.Property(o => o.LostReasonKey).HasMaxLength(60);
            e.Property(o => o.PerilKey).HasMaxLength(60);
            e.Property(o => o.ExpectedValue).HasPrecision(12, 2);
            e.Property(o => o.Description).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
            e.HasIndex(o => new { o.TenantId, o.StageId });
            e.HasIndex(o => new { o.TenantId, o.OwnerUserId });
            e.HasIndex(o => new { o.TenantId, o.PropertyId });
            e.HasIndex(o => new { o.TenantId, o.PrimaryContactId });
            e.HasIndex(o => new { o.TenantId, o.Outcome, o.RecycleAt });
            e.HasOne<Disposition>().WithMany().HasForeignKey(o => o.LastDispositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(o => o.PrimaryContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(o => o.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PipelineStage>().WithMany().HasForeignKey(o => o.StageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Disposition>(e =>
        {
            e.ToTable("dispositions");
            e.Property(d => d.Name).HasMaxLength(100);
            e.Property(d => d.Color).HasMaxLength(20);
            e.Property(d => d.LostReasonKey).HasMaxLength(60);
            e.HasIndex(d => new { d.TenantId, d.Sort });
            e.HasOne<PipelineStage>().WithMany().HasForeignKey(d => d.MovesToStageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Description).HasMaxLength(500);
            e.Property(c => c.Mode).HasMaxLength(20);
            e.Property(c => c.CriteriaJson).HasMaxLength(4000);
            e.HasIndex(c => new { c.TenantId, c.IsActive });
        });

        b.Entity<CampaignMember>(e =>
        {
            e.ToTable("campaign_members");
            e.HasIndex(m => new { m.TenantId, m.CampaignId, m.OpportunityId }).IsUnique();
            e.HasIndex(m => new { m.TenantId, m.OpportunityId });
            e.HasOne<Campaign>().WithMany().HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Opportunity>().WithMany().HasForeignKey(m => m.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Domain.Reports.SavedReport>(e =>
        {
            e.ToTable("saved_reports");
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Kind).HasMaxLength(30);
            e.Property(r => r.RangeKey).HasMaxLength(20);
            e.Property(r => r.Schedule).HasMaxLength(10);
            e.Property(r => r.LastError).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.UserId });
            e.HasIndex(r => r.NextSendAt);
        });

        b.Entity<SavedView>(e =>
        {
            e.ToTable("saved_views");
            e.Property(v => v.ListKey).HasMaxLength(40);
            e.Property(v => v.Name).HasMaxLength(80);
            e.Property(v => v.CriteriaJson).HasMaxLength(2000);
            e.HasIndex(v => new { v.TenantId, v.ListKey, v.UserId });
        });

        b.Entity<OpportunityDisposition>(e =>
        {
            e.ToTable("opportunity_dispositions");
            e.Property(d => d.Note).HasMaxLength(500);
            e.HasIndex(d => new { d.TenantId, d.OpportunityId });
            e.HasIndex(d => new { d.TenantId, d.ByUserId, d.CreatedAt });
            e.HasOne<Opportunity>().WithMany().HasForeignKey(d => d.OpportunityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Disposition>().WithMany().HasForeignKey(d => d.DispositionId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OpportunityStageChange>(e =>
        {
            e.ToTable("opportunity_stage_changes");
            e.Property(c => c.Note).HasMaxLength(500);
            e.HasIndex(c => new { c.TenantId, c.OpportunityId });
        });

        b.Entity<LookupValue>(e =>
        {
            e.ToTable("lookup_values");
            e.Property(l => l.Type).HasMaxLength(60);
            e.Property(l => l.Key).HasMaxLength(60);
            e.Property(l => l.Label).HasMaxLength(100);
            e.Property(l => l.Color).HasMaxLength(20);
            e.HasIndex(l => new { l.TenantId, l.Type, l.Key }).IsUnique();
        });

        b.Entity<Note>(e =>
        {
            e.ToTable("notes");
            e.Property(n => n.Body).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(n => n.EntityType).HasMaxLength(30);
        });

        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(d => d.StorageKey).HasMaxLength(120);
            e.Property(d => d.FileName).HasMaxLength(255);
            e.Property(d => d.ContentType).HasMaxLength(120);
            e.Property(d => d.Sha256).HasMaxLength(64);
            e.Property(d => d.CategoryKey).HasMaxLength(60);
            e.Property(d => d.Source).HasMaxLength(20);
            e.Property(d => d.EntityType).HasMaxLength(30);
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.ToTable("activity_events");
            e.Property(a => a.EventType).HasMaxLength(60);
            e.Property(a => a.Summary).HasMaxLength(500);
            e.Property(a => a.EntityType).HasMaxLength(30);
            e.Property(a => a.DataJson).HasColumnType("json").Metadata.SetMaxLength(null);
        });

        b.Entity<RecordLink>(e =>
        {
            e.ToTable("record_links");
            e.Property(l => l.ItemType).HasMaxLength(20);
            e.Property(l => l.EntityType).HasMaxLength(30);
            e.HasIndex(l => new { l.TenantId, l.EntityType, l.EntityId, l.ItemType });
            e.HasIndex(l => new { l.TenantId, l.ItemType, l.ItemId, l.EntityType, l.EntityId }).IsUnique();
        });
    }

    /// <summary>
    /// Adds the named tenant filter to every ITenantOwned entity. New entities get it
    /// automatically; the isolation tests fail if any tenant-owned type is missing it.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder b)
    {
        var method = typeof(OesDbContext).GetMethod(nameof(ApplyTenantFilter),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        foreach (var entityType in b.Model.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                method.MakeGenericMethod(entityType.ClrType).Invoke(this, [b]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned =>
        b.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
}

public sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    d => d.ToDateTime(TimeOnly.MinValue),
    v => DateOnly.FromDateTime(v));

/// <summary>All times are stored in UTC; this marks values read back as UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

/// <summary>Time of day as whole minutes since midnight.</summary>
public sealed class TimeOnlyMinutesConverter() : ValueConverter<TimeOnly, int>(
    t => t.Hour * 60 + t.Minute,
    m => new TimeOnly(m / 60, m % 60));
