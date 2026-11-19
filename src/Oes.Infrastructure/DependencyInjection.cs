using Amazon;
using Amazon.SimpleEmailV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Mail;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOesInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Oes")
            ?? throw new InvalidOperationException("Connection string 'Oes' is not configured.");

        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<OesDbContext>((sp, o) =>
        {
            o.UseMySQL(connectionString, my => my.MigrationsAssembly(typeof(OesDbContext).Assembly.FullName));
            o.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<INumberSequence, NumberSequence>();
        services.AddScoped<MembershipLookup>();
        services.AddScoped<TenantProvisioner>();
        services.AddScoped<Admin.TenantAdminService>();

        services.AddScoped<Crm.RecordScope>();
        services.AddScoped<Crm.RecordItems>();
        services.AddScoped<Crm.ContactService>();
        services.AddScoped<Crm.MergeService>();
        services.AddScoped<Crm.PropertyService>();
        services.AddScoped<Crm.OpportunityService>();
        services.AddScoped<Crm.CrmLookups>();
        services.AddScoped<Crm.ListSettingsService>();
        services.AddScoped<Crm.ProspectingService>();
        services.AddScoped<Crm.SavedViewService>();
        services.AddScoped<Crm.CampaignQuery>();
        services.AddScoped<Crm.CampaignService>();

        services.AddScoped<Insurance.CarrierService>();
        services.AddScoped<Insurance.StormService>();
        services.AddScoped<Insurance.ClaimService>();
        services.AddScoped<Measurements.MeasurementService>();
        services.AddScoped<Estimating.CatalogService>();
        services.AddScoped<Estimating.EstimateService>();
        services.AddScoped<Estimating.TakeoffService>();
        services.AddScoped<Production.ProjectService>();
        services.AddScoped<Security.AccessTokenService>();
        services.AddScoped<Subcontractors.SubcontractorService>();
        services.AddScoped<Subcontractors.WorkOrderService>();
        services.AddScoped<Subcontractors.PortalService>();
        services.AddScoped<Work.TenantTime>();
        services.AddScoped<Work.NotificationService>();
        services.AddScoped<Work.WorkService>();
        services.AddScoped<Work.ScheduledChecks>();
        services.AddScoped<Work.NightlyCleanup>();
        services.AddScoped<Work.AutomationService>();
        services.AddScoped<Work.AutomationRunner>();

        services.Configure<Ai.AiOptions>(config.GetSection(Ai.AiOptions.Section));
        var aiProvider = config.GetSection(Ai.AiOptions.Section)["Provider"];
        if (string.Equals(aiProvider, "Anthropic", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<IAiClient, Ai.AnthropicAiClient>();
        else services.AddSingleton<IAiClient, Ai.NotConfiguredAiClient>();
        services.AddScoped<Ai.AiUsageMeter>();
        services.AddScoped<Ai.MeasurementExtractor>();
        services.AddScoped<Ai.ScopeImportService>();
        services.AddScoped<Ai.AssistantTools>();
        services.AddScoped<Ai.AssistantService>();

        services.Configure<Communication.MessagingOptions>(config.GetSection(Communication.MessagingOptions.Section));
        var messaging = config.GetSection(Communication.MessagingOptions.Section).Get<Communication.MessagingOptions>() ?? new Communication.MessagingOptions();
        if (string.Equals(messaging.MailProvider, "Dev", StringComparison.OrdinalIgnoreCase)) services.AddScoped<IMailProvider, Communication.DevMailProvider>();
        else services.AddScoped<IMailProvider, Communication.NotConfiguredProvider>();
        if (string.Equals(messaging.SmsProvider, "Dev", StringComparison.OrdinalIgnoreCase)) services.AddScoped<ISmsProvider, Communication.DevSmsProvider>();
        else services.AddScoped<ISmsProvider, Communication.NotConfiguredProvider>();
        services.AddScoped<Communication.MergeFields>();
        services.AddScoped<Communication.TemplateService>();
        services.AddScoped<Communication.ConsentService>();
        services.AddScoped<Communication.MessagingService>();
        services.AddScoped<Communication.BlastService>();
        services.AddScoped<Communication.BlastSender>();
        services.AddScoped<Communication.NurtureService>();
        services.AddScoped<Communication.NurtureRunner>();
        services.AddScoped<Admin.SettingsService>();
        services.AddScoped<Scheduling.BookingService>();
        services.AddScoped<Reports.ReportService>();

        services.Configure<FileStoreOptions>(config.GetSection(FileStoreOptions.Section));
        services.AddScoped<IFileStore, DiskFileStore>();

        services.Configure<SystemMailOptions>(config.GetSection(SystemMailOptions.Section));
        var mail = config.GetSection(SystemMailOptions.Section).Get<SystemMailOptions>() ?? new SystemMailOptions();
        if (string.Equals(mail.Provider, "Ses", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ => new AmazonSimpleEmailServiceV2Client(
                RegionEndpoint.GetBySystemName(mail.SesRegion ?? throw new InvalidOperationException("SystemMail:SesRegion is required for SES."))));
            services.AddScoped<ISystemMailer, SesSystemMailer>();
        }
        else
        {
            services.AddScoped<ISystemMailer, PickupSystemMailer>();
        }

        return services;
    }
}
