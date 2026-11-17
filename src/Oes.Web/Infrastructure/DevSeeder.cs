using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Web.Infrastructure;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string? AdminEmail { get; set; }

    /// <summary>Set with `dotnet user-secrets`, never in appsettings.</summary>
    public string? AdminPassword { get; set; }

    public string[] TenantNames { get; set; } = ["Demo Roofing", "Second Demo Co"];
}

/// <summary>
/// Development only: applies migrations, creates demo tenants and an admin who belongs to
/// all of them (to exercise the tenant picker), makes sure every tenant has its default
/// stages and pick lists, and adds sample CRM records to the first demo tenant.
/// </summary>
public static class DevSeeder
{
    public static async Task RunAsync(IServiceProvider root, CancellationToken ct = default)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder");
        var db = sp.GetRequiredService<OesDbContext>();

        await db.Database.MigrateAsync(ct);

        var opts = sp.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new();
        if (!await db.Users.AnyAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(opts.AdminEmail) || string.IsNullOrWhiteSpace(opts.AdminPassword))
            {
                log.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set (use dotnet user-secrets). Skipping seed.");
                return;
            }

            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var admin = new AppUser
            {
                UserName = opts.AdminEmail, Email = opts.AdminEmail, EmailConfirmed = true,
                FirstName = "Demo", LastName = "Admin",
            };
            var created = await users.CreateAsync(admin, opts.AdminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException("Seed admin not created: " + string.Join("; ", created.Errors.Select(e => e.Description)));

            foreach (var name in opts.TenantNames)
            {
                // Each tenant needs its own scope: a scope's tenant context is set once.
                using var tScope = root.CreateScope();
                var provisioner = tScope.ServiceProvider.GetRequiredService<TenantProvisioner>();
                var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                await provisioner.ProvisionAsync(new ProvisionTenantRequest(name, slug, "America/Chicago", admin), ct);
                log.LogInformation("Seeded tenant {Tenant}", name);
            }
        }

        // Defaults added in later milestones reach tenants created before them.
        var tenantIds = await db.Tenants.OrderBy(t => t.CreatedAt).Select(t => t.Id).ToListAsync(ct);
        foreach (var tenantId in tenantIds)
        {
            using var tScope = root.CreateScope();
            tScope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
            var tdb = tScope.ServiceProvider.GetRequiredService<OesDbContext>();
            await TenantDefaults.EnsureAsync(tdb, tenantId, ct);
            await tdb.SaveChangesAsync(ct);
        }

        if (tenantIds.Count > 0)
        {
            await SampleDataAsync(root, tenantIds[0], log, ct);
            await SampleInsuranceAsync(root, tenantIds[0], log, ct);
            await SampleMeasurementsAsync(root, tenantIds[0], log, ct);
        }
    }

    /// <summary>Two roofing versions and a gutter set on 123 Main St.</summary>
    private static async Task SampleMeasurementsAsync(IServiceProvider root, Guid tenantId, ILogger log, CancellationToken ct)
    {
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OesDbContext>();
        if (await db.MeasurementSets.AnyAsync(ct)) return;
        var property = await db.Properties.Include(p => p.Structures).FirstOrDefaultAsync(p => p.Line1 == "123 Main St", ct);
        if (property is null) return;
        var structure = property.Structures.OrderBy(s => s.Sort).First();
        var roofing = await db.TradeTypes.FirstAsync(t => t.Code == "roofing", ct);
        var gutters = await db.TradeTypes.FirstAsync(t => t.Code == "gutters", ct);

        Oes.Domain.Measurements.MeasurementSet Set(Guid trade, int version, bool current, string source, DateOnly on, Guid? supersedes, params (string Field, decimal Value)[] values)
        {
            var set = new Oes.Domain.Measurements.MeasurementSet
            {
                PropertyId = property.Id, StructureId = structure.Id, TradeTypeId = trade, VersionNo = version, IsCurrent = current,
                Source = source, MeasuredOn = on, SupersedesId = supersedes,
            };
            set.Values.AddRange(values.Select(v => new Oes.Domain.Measurements.MeasurementValue { SetId = set.Id, FieldKey = v.Field, Value = v.Value }));
            db.MeasurementSets.Add(set);
            return set;
        }

        var tape = Set(roofing.Id, 1, false, "field_tape", new DateOnly(2026, 5, 18), null,
            ("total_area", 2480m), ("predominant_pitch", 6m), ("ridges", 48m), ("valleys", 36m), ("eaves", 120m), ("rakes", 88m));
        Set(roofing.Id, 2, true, "eagleview", new DateOnly(2026, 5, 22), tape.Id,
            ("total_area", 2612m), ("pitched_area", 2612m), ("predominant_pitch", 7m), ("facets", 9m), ("layers", 1m),
            ("ridges", 52m), ("hips", 0m), ("valleys", 41m), ("rakes", 92m), ("eaves", 126m), ("step_flashing", 18m),
            ("pipe_jacks", 3m), ("vents", 4m), ("suggested_waste", 12m));
        Set(gutters.Id, 1, true, "field_tape", new DateOnly(2026, 5, 18), null,
            ("gutter_length", 126m), ("downspout_length", 54m), ("downspouts", 5m), ("inside_miters", 2m), ("outside_miters", 4m));

        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded sample measurements");
    }

    /// <summary>A storm plus a claim on the Smith hail opportunity.</summary>
    private static async Task SampleInsuranceAsync(IServiceProvider root, Guid tenantId, ILogger log, CancellationToken ct)
    {
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OesDbContext>();
        if (await db.StormEvents.AnyAsync(ct)) return;

        var storm = new Oes.Domain.Insurance.StormEvent
        {
            Name = "May 14 2026 hail – Lincoln", EventDate = new DateOnly(2026, 5, 14), PerilKey = "hail",
            MaxHailSizeIn = 1.75m, MaxWindMph = 60, ZipCodes = "68502, 68508, 68510, 68516",
            Notes = "Golf-ball hail across south and central Lincoln, 4:10–4:45 pm.",
        };
        db.StormEvents.Add(storm);

        foreach (var o in await db.Opportunities.Where(o => o.IsInsurance).ToListAsync(ct)) o.StormEventId = storm.Id;

        var smith = await db.Opportunities.FirstOrDefaultAsync(o => o.Name.StartsWith("Smith hail"), ct);
        if (smith is not null)
        {
            var stateFarm = Guid.Parse("00000000-0000-7000-8000-000000000022");
            var tc = new Oes.Domain.Insurance.TenantCarrier { CarrierId = stateFarm, Notes = "Sample: supplements go to the desk adjuster by email with photos attached." };
            db.TenantCarriers.Add(tc);
            var claim = new Oes.Domain.Insurance.InsuranceClaim
            {
                OpportunityId = smith.Id, PropertyId = smith.PropertyId, TenantCarrierId = tc.Id, StormEventId = storm.Id,
                ClaimNumber = "13-48B2-77X", PolicyNumber = "SF-99-1234", DateOfLoss = storm.EventDate, PerilKey = "hail",
                Deductible = 1000m, Status = Oes.Domain.Insurance.ClaimStatuses.Approved, FiledOn = new DateOnly(2026, 5, 20),
                InspectionOn = new DateOnly(2026, 6, 2), ApprovedAmount = 16850m,
            };
            db.Claims.Add(claim);
            db.Supplements.Add(new Oes.Domain.Insurance.Supplement
            {
                ClaimId = claim.Id, Reason = "Drip edge and starter course", Status = Oes.Domain.Insurance.SupplementStatuses.Sent,
                RequestedAmount = 1240m, SentOn = new DateOnly(2026, 6, 10),
            });
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded sample insurance data");
    }

    /// <summary>A handful of contacts, properties and opportunities so the CRM screens have something to show.</summary>
    private static async Task SampleDataAsync(IServiceProvider root, Guid tenantId, ILogger log, CancellationToken ct)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = sp.GetRequiredService<OesDbContext>();
        if (await db.Contacts.AnyAsync(ct)) return;

        var ownerId = await db.Memberships.OrderBy(m => m.CreatedAt).Select(m => m.UserId).FirstAsync(ct);
        var stages = await db.PipelineStages.OrderBy(s => s.Sort).ToListAsync(ct);
        var numbers = sp.GetRequiredService<INumberSequence>();

        Contact Person(string first, string last, string email, string phone, string status = ContactStatuses.Lead)
        {
            var c = new Contact { FirstName = first, LastName = last, OwnerUserId = ownerId, Status = status, SourceKey = "door_knock" };
            c.RefreshDisplayName();
            c.Methods.Add(new ContactMethod { ContactId = c.Id, Kind = ContactMethodKinds.Email, Value = email, NormalizedValue = email, IsPrimary = true });
            c.Methods.Add(new ContactMethod { ContactId = c.Id, Kind = ContactMethodKinds.Mobile, Value = phone, NormalizedValue = ContactValue.Phone(phone), IsPrimary = true });
            db.Contacts.Add(c);
            return c;
        }

        Property House(string line1, string city, string zip, string kind = PropertyKinds.Residential)
        {
            var p = new Property { Line1 = line1, City = city, State = "NE", PostalCode = zip, Kind = kind, CreatedByUserId = ownerId };
            p.NormalizedKey = AddressKey.For(p.Line1, p.Line2, p.City, p.State, p.PostalCode);
            p.Structures.Add(new PropertyStructure { PropertyId = p.Id, Name = "Main House", Kind = "house", Sort = 10 });
            db.Properties.Add(p);
            return p;
        }

        void Link(Property p, Contact c, string role = "owner", bool primary = true) =>
            db.PropertyContacts.Add(new PropertyContact { PropertyId = p.Id, ContactId = c.Id, RoleKey = role, IsPrimary = primary, StartedOn = new DateOnly(2024, 4, 1) });

        async Task Opp(Contact c, Property? p, string name, int stageIndex, bool insurance, decimal? value)
        {
            var stage = stages[stageIndex];
            var o = new Opportunity
            {
                Number = await numbers.NextAsync(NumberedKinds.Opportunity, ct), Name = name, PrimaryContactId = c.Id, PropertyId = p?.Id,
                OwnerUserId = ownerId, StageId = stage.Id, Outcome = stage.Outcome, StageChangedAt = DateTime.UtcNow.AddDays(-stageIndex),
                IsInsurance = insurance, PerilKey = insurance ? "hail" : null, DateOfLoss = insurance ? new DateOnly(2026, 5, 14) : null,
                ExpectedValue = value, SourceKey = "storm_canvass",
            };
            if (stage.Outcome == Outcomes.Won) { o.WonAt = DateTime.UtcNow; o.ClosedAt = DateTime.UtcNow; }
            db.Opportunities.Add(o);
            db.OpportunityStageChanges.Add(new OpportunityStageChange { OpportunityId = o.Id, ToStageId = stage.Id, ByUserId = ownerId });
        }

        var jane = Person("Jane", "Smith", "jane.smith@example.com", "(402) 555-0134");
        var mark = Person("Mark", "Smith", "mark.smith@example.com", "(402) 555-0135");
        var luis = Person("Luis", "Ortega", "luis.ortega@example.com", "(402) 555-0190", ContactStatuses.Customer);
        var amy = Person("Amy", "Chen", "amy.chen@example.com", "(531) 555-0101");
        var dana = Person("Dana", "Whitfield", "dana@acmepm.example.com", "(402) 555-0177");
        var acme = new Contact { Type = ContactTypes.Organization, OrganizationName = "Acme Property Management", OwnerUserId = ownerId };
        acme.RefreshDisplayName();
        db.Contacts.Add(acme);
        db.ContactRelationships.Add(new ContactRelationship { PersonContactId = dana.Id, OrganizationContactId = acme.Id, Title = "Portfolio Manager", IsPrimary = true });

        var main = House("123 Main St", "Lincoln", "68502");
        var oak = House("4410 Oak Ridge Dr", "Lincoln", "68516");
        var pine = House("88 Pine Ct", "Omaha", "68104");
        var plaza = House("2200 Commerce Plaza", "Lincoln", "68508", PropertyKinds.Commercial);

        Link(main, jane); Link(main, mark, "co_owner", false);
        Link(oak, luis);
        Link(pine, mark, "owner");
        Link(plaza, acme, "owner"); Link(plaza, dana, "property_manager", false);

        await Opp(jane, main, "Smith hail claim – roof & gutters", 4, true, 18500m);
        await Opp(luis, oak, "Ortega roof replacement", 6, false, 14200m);
        await Opp(amy, null, "Amy Chen – storm inspection request", 0, true, null);
        await Opp(acme, plaza, "Commerce Plaza flat roof", 2, false, 96000m);
        await Opp(mark, pine, "Pine Ct rental – siding", 1, true, 9800m);

        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded sample CRM data");
    }
}
