using Oes.Infrastructure;
using Oes.Worker;

// Background job host. Runs as a Windows Service on the VPS (separate from IIS, whose
// app-pool recycles would kill long jobs). Jobs are plain hosted services on timers for now;
// each one works per tenant in its own DI scope. Hangfire can replace the timers when jobs
// need retries with backoff and a dashboard.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "OES4 Worker");
builder.Services.AddOesInfrastructure(builder.Configuration);
// The shared registrations include web-only services (user admin needs ASP.NET Identity), which
// jobs never resolve. Keep scope checks, but don't fail startup validating services jobs don't use.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));
builder.Services.AddHostedService<Heartbeat>();
builder.Services.AddHostedService<BlastJob>();

builder.Build().Run();
