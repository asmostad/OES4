using Oes.Worker;

// Background job host. Runs as a Windows Service on the VPS (separate from IIS, whose
// app-pool recycles would kill long jobs). Hangfire and the job catalog arrive with the
// milestones that need them (mail sync, nurture, blasts); for now it only reports health.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "OES4 Worker");
builder.Services.AddHostedService<Heartbeat>();

builder.Build().Run();
