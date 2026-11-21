# OES4

Multi-tenant rebuild of OES3, the roofing and restoration CRM. Planning docs are in [docs/](docs/). Start with [oes4-design-decisions.md](docs/oes4-design-decisions.md) and [oes4-solution-architecture.md](docs/oes4-solution-architecture.md).

## Stack

.NET 10 (LTS), ASP.NET Core Razor Pages + `/api/v1`, EF Core 10 with MySQL 9 (Oracle `MySql.EntityFrameworkCore`), ASP.NET Core Identity, Serilog, xUnit + Testcontainers.

## Layout

| Path | What |
|---|---|
| `src/Oes.Domain` | Entities and rules (no EF, no web) |
| `src/Oes.Application` | Permission catalog, interfaces (`ITenantContext`, `IFileStore`, `ISystemMailer`, …) |
| `src/Oes.Infrastructure` | EF Core context, tenant filter and write guard, migrations, file store, SES mailer, admin services |
| `src/Oes.Web` | Razor Pages UI (OES3 look), API controllers, auth, tenant resolution |
| `src/Oes.Worker` | Windows Service for background jobs (heartbeat only for now) |
| `tests/Oes.UnitTests` | Fast tests, no database |
| `tests/Oes.Integration.Tests` | Real MySQL in Docker: the tenant-isolation suite and end-to-end security tests |

## Run it locally

Prerequisites: .NET SDK 10.0.404 or later (see `global.json`) and Docker Desktop.

1. Start the dev database:

   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```

2. Set the demo admin password. It's kept in user-secrets and never committed:

   ```bash
   dotnet user-secrets set "Seed:AdminPassword" "<choose a 12+ character password>" --project src/Oes.Web
   ```

3. Run the web app:

   ```bash
   dotnet run --project src/Oes.Web --launch-profile http
   ```

   - In Development, the app applies migrations on startup.
   - On the first run it creates two demo companies and an admin, `admin@demo.test` (email set in `appsettings.Development.json`), who belongs to both. Use it to try the company picker.
   - System email (password resets, invites) goes to `.devdata/mail` as `.html` files instead of being sent.
   - The record-level header preview is at `/Dev/Levels`.

## Tests

```bash
dotnet test OES4.slnx
```

Docker must be running for the integration tests.

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Oes.Infrastructure --output-dir Persistence/Migrations
```
