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
        services.AddScoped<Crm.PropertyService>();
        services.AddScoped<Crm.OpportunityService>();
        services.AddScoped<Crm.CrmLookups>();
        services.AddScoped<Crm.ListSettingsService>();

        services.AddScoped<Insurance.CarrierService>();
        services.AddScoped<Insurance.StormService>();
        services.AddScoped<Insurance.ClaimService>();
        services.AddScoped<Measurements.MeasurementService>();

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
