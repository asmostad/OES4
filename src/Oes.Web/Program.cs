using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;
using Oes.Web.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

builder.Services.AddOesInfrastructure(builder.Configuration);

// Identity: platform-level users; tenant roles and permissions are our own tables.
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 12;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<OesDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "oes.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    // The API answers with status codes, never login redirects.
    o.Events.OnRedirectToLogin = ctx => ApiAware(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status401Unauthorized);
    o.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status403Forbidden);
});
// Keep the chosen tenant when Identity refreshes the cookie (security stamp check).
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(5);
    o.OnRefreshingPrincipal = ctx =>
    {
        var tenant = ctx.CurrentPrincipal?.FindFirst(OesClaims.TenantId);
        if (tenant is not null && ctx.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity id)
            id.AddClaim(new System.Security.Claims.Claim(OesClaims.TenantId, tenant.Value));
        return Task.CompletedTask;
    };
});

builder.Services.AddScoped<Shell>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "oes.csrf";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

// Everything requires a signed-in user (fallback policy) except these pages.
builder.Services.AddRazorPages(o => o.Conventions
    .AllowAnonymousToPage("/Account/Login")
    .AllowAnonymousToPage("/Account/AccessDenied")
    .AllowAnonymousToPage("/Account/ForgotPassword")
    .AllowAnonymousToPage("/Account/ResetPassword")
    .AllowAnonymousToPage("/Error"));
builder.Services.AddControllers(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");

var loginPerMinute = builder.Configuration.GetValue("RateLimits:LoginPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddHealthChecks().AddDbContextCheck<OesDbContext>("database");

var keysPath = builder.Configuration["DataProtection:KeysPath"];
var dp = builder.Services.AddDataProtection().SetApplicationName("OES4");
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await DevSeeder.RunAsync(app.Services);
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<RequireTenantMiddleware>();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

app.Run();

static Task ApiAware(HttpContext http, string redirectUri, int status)
{
    if (http.Request.Path.StartsWithSegments("/api")) http.Response.StatusCode = status;
    else http.Response.Redirect(redirectUri);
    return Task.CompletedTask;
}

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
