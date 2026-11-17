# OES4 Solution Architecture (Draft v1)

**Status:** draft for review, 2026-11-16. It builds on [oes4-design-decisions.md](oes4-design-decisions.md) and [oes4-schema.md](oes4-schema.md).

## 1. Stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# | Current LTS; native on Windows/IIS; also runs on Linux later |
| Web UI | **ASP.NET Core Razor Pages**, Bootstrap 5.3.3, Bootstrap Icons, Inter, ported `oes3.css` tokens/components, vanilla JS modules (port of `oes3.js`) | Server-rendered, like OES3's `.cfm` pages, so the look carries over almost 1:1 |
| API | ASP.NET Core controllers under `/api/v1`, OpenAPI document, ProblemDetails errors | The same API serves web JS and the future mobile apps |
| Data | **EF Core 10** + MySQL 9.6 | Global tenant query filters, migrations, concurrency tokens |
| Auth | ASP.NET Core Identity (cookie) + **OpenIddict** (OAuth2/OIDC with refresh tokens for mobile) | Lockout, MFA-ready, proper password hashing |
| Background jobs | **Hangfire** (MySQL storage) in a separate **Worker** Windows Service | IIS recycles app pools, so jobs shouldn't live in the web process |
| Real-time | SignalR | Notification badge, lead locks, live board updates |
| PDF | **Playwright (headless Chromium)** rendering a Razor template to HTML in-process, then to PDF | Keeps the OES3 HTML/CSS proposal design. No public print URLs or keys. |
| Email to contacts | Microsoft Graph per tenant (OAuth admin consent), behind `IMailProvider` | Decided: the tenant's own M365; Gmail etc. later |
| System email | **Amazon SES** behind `ISystemMailer` | Decided: from our own domain |
| SMS | Twilio (per-tenant number/subaccount) behind `ISmsProvider` | Webhooks route by "To" number |
| Files | `IFileStore` with **`DiskFileStore`** now (private folder outside the web root), `S3FileStore` later | Decided: disk now, cloud later with no code changes |
| AI | Anthropic API behind `IAiClient`; per-tenant key or platform key plus a usage budget | Assistant, report extraction, scope import |
| Logging | Serilog (file + optional Seq), correlation IDs, health checks | |
| Validation | FluentValidation | Same rules for pages and API |
| Tests | xUnit, Testcontainers (MySQL in Docker) for integration tests, Playwright for UI smoke tests | Docker is already on this machine |

> **Settled at setup (2026-11-16):**
> - **.NET SDK 10.0.404** is installed and pinned in `global.json`. .NET 11 shipped in November 2026, but it is a short-term-support (STS) release, so we stay on the 10 LTS. All Microsoft packages are pinned to 10.0.x.
> - **MySQL provider:** Oracle `MySql.EntityFrameworkCore` 10.0.x. The official Pomelo release does not support EF Core 10 yet (only a fork does). The provider is configured in one place (`DependencyInjection.cs`), so we can switch back to Pomelo if it catches up.
> - GUIDs are stored as `BINARY(16)` in big-endian byte order through our own converter, so v7 IDs stay in time order regardless of provider.
> - **OpenIddict** (mobile tokens) is deferred to milestone 13. Until then the API uses the same auth cookie and anti-forgery header as the web pages.

## 2. Solution layout

```
OES4/
├─ OES4.sln
├─ src/
│  ├─ Oes.Domain/            Entities, enums, value objects, domain rules (no EF, no web)
│  ├─ Oes.Application/       Use-case services, DTOs, permissions catalog, validators,
│  │                         authorization policies, merge-field resolver, interfaces
│  │                         (IFileStore, IMailProvider, ISystemMailer, ISmsProvider, IAiClient,
│  │                          IPdfRenderer, IClock, ITenantContext, ICurrentUser)
│  ├─ Oes.Infrastructure/    EF Core DbContext + configurations + migrations, tenant filters &
│  │                         interceptors, DiskFileStore, Graph/SES/Twilio/Anthropic clients,
│  │                         Playwright PDF renderer, Hangfire job definitions, encryption
│  ├─ Oes.Web/               Razor Pages (UI), /api/v1 controllers, Identity + OpenIddict,
│  │                         SignalR hubs, public endpoints (booking, sub portal, unsubscribe,
│  │                         webhooks), static assets (oes4.css, oes4.js)
│  └─ Oes.Worker/            Windows Service host for Hangfire jobs (sync, nurture, blasts,
│                            notifications, reminders, cleanup)
├─ tests/
│  ├─ Oes.UnitTests/          Fast tests with no database (domain, permissions, file store, rules)
│  └─ Oes.Integration.Tests/  Real MySQL via Testcontainers: tenant-isolation suite, end-to-end
│                             auth/permission/API tests (Playwright UI smoke tests later)
├─ tools/
│  └─ Oes.Migrator/           OES3 → OES4 data migration (JRS = tenant #1), re-runnable
├─ docs/
└─ mobile/                    (later) React Native/Expo app against /api/v1
```

**Dependency rule:** Web and Worker → Infrastructure → Application → Domain. Pages and controllers stay thin and call Application services, so web and mobile run exactly the same business logic and permission checks.

## 3. Multi-tenancy

- **Tenant context.** At login the user picks a tenant (if they belong to more than one). The tenant ID goes into the auth cookie or token as a claim. `ITenantContext` reads it once per request or job.
- **Query isolation.** Every tenant-owned entity implements `ITenantOwned`. A global query filter applies `TenantId == current` to every query automatically.
- **Write isolation.** A `SaveChanges` interceptor stamps `TenantId` on inserts and **throws** if an update or delete targets another tenant's row.
- **Background jobs** carry the tenant ID explicitly in each job's arguments and set the context before running. No "all tenants" query unless a platform job opts in with an audited `IgnoreTenantFilter()` scope.
- **Public endpoints** resolve the tenant from the hashed access token (sub portal, booking manage, unsubscribe), the booking slug (`/book/{tenantSlug}/{type}`), or the webhook path token or phone number. Never from a query-string ID.
- **Proof.** An integration test suite creates two tenants and asserts that every repository, page and API endpoint refuses cross-tenant access. It runs in CI on every change.

## 4. Authorization

1. **Permission policies.** Each endpoint or page declares the permission it needs (e.g. `estimates.manage`). The permission keys are defined in code (`Permissions.cs`), synced to the `permissions` table, and assigned to roles per tenant.
2. **Record scope (resource-based).** A handler checks "can this user see or edit *this* property/opportunity/estimate/project?" (own vs all, assigned role, unassigned pool). It is used by pages, API endpoints, file downloads, search **and AI tools**. OES3 had no record-level view checks at all.
3. **No role-name checks anywhere.** A unit test fails the build if `"admin"`-style role strings appear in authorization code.

## 5. Security baseline (built into the template, not added per feature)

- Anti-forgery on all form posts, plus a header token for JS fetches. **State changes only on POST/PUT/DELETE.**
- Razor auto-encoding. JS helpers build DOM with `textContent`; never `innerHTML` with data. LLM output is treated as untrusted.
- File uploads: extension and content-type allow-list, size limits, stored under a random key outside the web root, served by an authorized download endpoint with `Content-Disposition`.
- Secrets: Data Protection-encrypted in the database, write-only in settings screens (show "set / not set").
- Rate limiting (ASP.NET Core rate limiter) on login, booking, slots, portal and webhooks.
- Outbound HTTP to user-supplied URLs (ICS calendars, images) goes through an SSRF-safe client (DNS pin, block private ranges, no redirects off-host, size and time caps).
- Webhooks: per-tenant path token + HMAC signature + timestamp window + constant-time compare + event-ID idempotency.
- Security headers: CSP, `Referrer-Policy: no-referrer` on token pages, HSTS.

## 6. UI structure (keeping the OES3 look)

- **Shell:** the same fixed dark sidebar, sticky topbar, global search, quick-add, notification bell and user menu. Tenant logo replaces the JRS logo.
- **Record-level identity:** a shared `_RecordHeader` partial takes the level, so each level gets its own accent color, icon, header band and layout.

  | Level | Accent / icon |
  |---|---|
  | Property | slate / house |
  | Contact | blue / person |
  | Organization | indigo / building |
  | Opportunity | violet / funnel |
  | Estimate | amber / calculator |
  | Project | green / hammer |
  | Trade | teal / layers |
- **Breadcrumb** built from the real parent chain, e.g. `Property › Project › Trade`, with the primary contact as a chip and a one-click "up a level".
- **Level dashboards:** each level gets its own overview layout. Tabs (Notes, Documents, Email, SMS, Tasks, Appointments, Activity) are shared components with a "This record / Everything under it" filter backed by `record_links`.
- **Assets:** `oes4.css` is a refactor of `oes3.css` (tokens kept, hard-coded colors moved into tokens, dark mode optional later). `oes4.js` is ES modules ported from `oes3.js` (fetch helpers with anti-forgery, toasts via `textContent`, typeahead, upload zones, kanban via SortableJS).

## 7. Background jobs (Worker)

| Job | Schedule |
|---|---|
| M365 mail delta sync, per mailbox | every 5–15 min |
| M365 calendar sync, per user | every 15 min, plus push on change |
| Busy-calendar ICS refresh | every 15 min |
| Nurture step runner | every minute |
| Blast sender (claims recipients atomically, throttled per tenant) | every minute |
| Notification checks (due soon, overdue, stale opportunities, expiring sub insurance) | every 15 min |
| Estimate expiry, opportunity recycle | nightly |
| Cleanup (old notifications, webhook payload retention, temp files) | nightly |
| AI document extraction / scope import | on demand, queued |

Every job is per tenant, retried with backoff, and visible on a super-admin dashboard (Hangfire dashboard behind super-admin auth).

## 8. Environments and deployment

- **Dev:** local .NET + MySQL in Docker.
- **Staging:** on the VPS as a second IIS site and database. Optional for now, but recommended before JRS cuts over.
- **Prod:** IIS site (ASP.NET Core Hosting Bundle) + `Oes.Worker` Windows Service + MySQL on the same VPS.
  - The file store is a folder outside `wwwroot`.
  - Nightly backups of MySQL plus the file store go off-box.
- **CI:** GitHub Actions builds, tests (including the tenant-isolation suite) and packages. Deploy is a zip/web-deploy step at first, later automated.
- **Config:** `appsettings.{env}.json` holds non-secrets. Secrets come from environment variables or user-secrets in dev. Data Protection keys are persisted to disk and protected with DPAPI on Windows.

## 9. Build order (proposed milestones)

1. **Foundation** (done 2026-11-16)
   - Solution skeleton; CI.
   - Tenancy + Identity + roles/permissions + tenant isolation tests.
   - App shell with the OES3 look; audit log; file store; SES system mail.
2. **CRM core** (most of it done 2026-11-16)
   - Done:
     - Contacts (person/org) and properties with structures, property ↔ contact links with roles and history.
     - Duplicate-address detection by normalized key.
     - Opportunities with numbering, a pipeline board (drag and drop), stage history, won/lost with a reason, and attaching a property later.
     - Notes, documents and activity with `record_links` rollups ("This record / Everything under it").
     - Record-level visibility (own + unassigned pool unless `records.view_all`).
     - Global search and typeahead pickers, level headers and breadcrumbs, a dashboard with pipeline numbers.
   - Still to do:
     - Dispositions and the prospecting back/next list with locks.
     - Saved views, campaigns, the property/contact merge tool.
     - Contact mailing addresses and consents (needed before SMS and blasts).
     - Admin screens for pipeline stages and pick lists (defaults are seeded).
3. **Insurance** (done 2026-11-17)
   - Carriers: a platform default list (names only), with tenant overrides kept private and falling back to the defaults; tenants can add their own carriers and hide unused ones.
   - Claims: optional until filed, and visibility follows the opportunity; notes roll up to the opportunity, property and contact.
   - Supplements with status, amounts and dates.
   - Storm events with ZIP codes, a canvass list and storm suggestions; storm filters on the pipeline and claims.
   - Admin screens for pipeline stages and pick lists.
4. **Measurements** (done 2026-11-17, except AI extraction)
   - Default trades and measurement fields per tenant, editable under Administration › Trades & Measurements.
   - Per-property measurements page by structure × trade.
   - Versions that are never overwritten, with a "vs previous" delta, revert by making an older version current, and the report file kept on the property.
   - Roof, elevation (per wall side) and linear modes.
   - Still to do: AI extraction from EagleView/Hover PDFs (needs the AI client from milestone 10).
5. **Estimating**
   - Catalog, price lists and importer, products.
   - Price-book builder, line-item/takeoff method.
   - Approvals, versions and signing, **proposal PDF**.
6. **Production**
   - Projects/trades, workflow stages with roles, kanban, automation rules, Remove from Board.
   - Change orders, order sheet.
7. **Subcontractors** — subs, rates, work orders, sub portal.
8. **Communication**
   - M365 mail + calendar sync, templates, SMS with consent/STOP, notifications (+ SignalR).
   - Tasks, calendar, appointments.
9. **Marketing** — nurture, email blasts + suppressions, online booking + busy calendars.
10. **AI assistant** — tools with record-scope enforcement, budgets, knowledge base.
11. **Reports** — pipeline, conversion, production, stale, tasks, with charts and CSV.
12. **JRS migration** — `Oes.Migrator` dry runs, review queues (duplicate properties/contacts), cutover.
13. **Mobile API hardening**, then the mobile app.
