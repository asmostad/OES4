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

Today the worker runs plain timed hosted services, each job working per tenant in its own DI scope: `BlastJob` and `NurtureJob` (every minute), `AutomationJob` (every minute: automation rules, see milestone 8), `ChecksJob` (every 15 minutes: tasks due within the hour and newly overdue, opportunities that just went stale, issued estimates expiring within 3 days or just expired, sub insurance expiring within 30 days or expired, day-before booking reminder emails) and `CleanupJob` (daily: read notifications after 90 days, unread after 180, public links 30 days past expiry or revocation). Alerts carry a dedupe key (with the date they are about) so they fire once; only events from the last 2 days alert, so switching it on does not flood anyone. Hangfire (retries with backoff, dashboard) replaces the timers when more jobs arrive. The worker skips validate-on-build because the shared registrations include web-only services (user admin needs ASP.NET Identity).

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
     - Contact mailing addresses (consents were added in milestone 8).
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
5. **Estimating** (core done 2026-11-17)
   - Done:
     - Catalog separate from many dated price lists, with a CSV importer, list copying and price editing; products.
     - Price-book and line-item estimates on one line model, with sections built from current measurements (one-click quantities) and prices copied onto lines.
     - O&P and tax (materials, all or none).
     - Approvals by exception (custom lines, below list, no price) with no self-approval.
     - Frozen versions on issue (JSON snapshot plus hash, a revision reason after v1), sign/decline, and signing wins the opportunity.
     - A printable proposal rendered from the frozen version.
   - Still to do:
     - Server-side PDF (Playwright).
     - The takeoff rules engine and order sheet.
     - Carrier scope import (AI).
     - Tenant default terms.
6. **Production** (core done 2026-11-17)
   - Done:
     - Projects from signed estimates (one trade per estimate trade, contract = signed total).
     - Per-tenant project and trade stages with roles, which are unique per level and editable.
     - Gates by role: material date before deliveries, work date before labor, all trades complete before the project completes.
     - Forward-only automation (production start, project complete) with a move preview and "automatic" history.
     - Project and trade kanban boards (drag and drop), Remove from Board with reasons and restore.
     - Change orders with customer approval that adds to the contract (and to the trade amount).
   - Still to do:
     - Configurable automation rules: done in milestone 8 (Administration › Automation).
     - Order sheet (with the takeoff engine).
     - Line-level change orders.
7. **Subcontractors** (done 2026-11-17)
   - Subs as organization contacts with a profile (trades, status).
   - Documents with expiry dates, with a cached earliest insurance expiry and badges.
   - Effective-dated rates: a new rate ends the old one.
   - Work orders on trades, with lines copied from the rates in effect on the work date.
   - Issuing emails a hashed, 60-day, revocable portal link; revise, new link and cancel all revoke it.
   - Sub portal (no sign-in, rate limited, no-referrer, noindex): accept or decline, then report completion with photos that roll up to the trade and project.
   - Work order print view (`/WorkOrders/Print/{id}`; print or save as PDF from the browser, like the proposal). Server-side PDF rendering for both comes with Playwright later. Insurance-expiry alerts are done (worker `ChecksJob`).
8. **Communication** (in progress: tasks, calendar, notifications, templates, one-to-one email and texts, consent and STOP done 2026-11-17)
   - Done:
     - Tasks: standalone or on any record, rolled up through record_links (a task on an opportunity shows on its property and contact). Date-only tasks are stored at noon in the company time zone; overdue/today grouping; mine/everyone (`tasks.view_all`); done-toggle logs to the record and notifies the creator.
     - Appointments: typed (lookup `appointment_type`), on records or standalone, location defaults to the property address, assigning others needs `calendar.manage`. Week calendar (Sunday start) with tasks due that week.
     - Record pages (opportunity, property, contact, claim, project, trade) show a Tasks and Appointments card through the `RecordAgenda` view component.
     - In-app notifications: bell with unread count and mark-all-read; links are app-relative only. Sent for task assignment/completion, price approvals needed (to everyone with `pricing.approve` except the requester), and sub portal accept/decline/complete (to the project owner). Never to yourself.
     - Dashboard "My day": overdue, due today, today's appointments.
     - Automation rules (Administration › Automation, `automation.manage`; done 2026-11-18): "when an opportunity / project / trade enters stage S, create a task (title with merge fields, due in N days, for the owner or a named user), send a notification, or enroll the contact in a nurture workflow." The worker's `AutomationJob` reads the stage-change history every minute, so every path that moves a record counts (board drag, record page, signing an estimate, production automation, a new record entering its first stage). Each rule runs once per move (unique rule + change), only for moves made after the rule was created and within the last two days; turning a rule off stops it. Every run is logged with its result on the Automation page.
     - Templates (email and text) with one merge-field resolver (`{{contact.first_name}}`, `{{property.address}}`, `{{user.name}}`…); unknown fields are rejected on save. Bodies are plain text; email is HTML-encoded on send, so no user HTML is ever mailed. Starter templates are seeded per company.
     - Email and texts from record pages (the `RecordMessaging` card): only to the record's contacts' own addresses, one row per message linked through record_links, shown on the timeline (Emails / Texts tabs). Outgoing messages are private to the sender unless the user has `email.view_all` / `sms.view_all`.
     - Consent and suppression: consent history per contact (latest wins), suppression per address. Texts need an opt-in; email is allowed unless opted out. Inbound STOP/START/HELP are handled with confirmation replies; a STOP can only be undone by the person (START), never by staff. The first text to a number adds "Reply STOP to opt out."
     - Company texting number (unique across companies) routes inbound texts to the right tenant. Replies continue on the record the last text came from and notify its sender.
     - Providers sit behind `IMailProvider` / `ISmsProvider`. Development writes to a pickup folder and has a secret-protected `/api/webhooks/sms/dev` endpoint; with no provider configured, sends fail with a clear message and nothing leaves the server.
   - Still to do (needs external credentials or later work):
     - Real providers: Microsoft Graph mail (per-user OAuth, inbound sync and threading) and calendar sync, Twilio texting (request-signature validation, A2P registration, delivery status callbacks). SignalR live updates, email/push delivery of notifications and preferences.
     - Unmatched inbound texts (number on no contact) are stored but not yet shown in an inbox; MMS; email CC/BCC and attachments.
     - Appointment attendees, all-day events. (Done since: editing tasks and appointments in place, with the assignee told when moved or handed over, and a booked appointment moving its booking; due/overdue, stale and expiry alerts; automation rules.)
9. **Marketing** (done 2026-11-17: email blasts, unsubscribe, suppressions, online booking, nurture workflows; provider-dependent parts listed below)
   - Done:
     - Email blasts: draft with a template or plain text and merge fields; audience by contact status, storm, lead source and organizations; a live count of who gets it (opted-out, unsubscribed, duplicate addresses and no-email contacts left out); "send me a test"; send now or later; cancel.
     - Scheduling locks the audience in as recipients (left-out people recorded with the reason). The worker's `BlastJob` sends each due blast in batches of its emails-per-minute setting, claims each recipient with a conditional update (pending to sending) so nothing is sent twice, re-checks consent just before sending, and marks interrupted sends failed instead of resending.
     - Every blast email has the company name and mailing address (Settings; scheduling is refused without it) and an unsubscribe link plus `List-Unsubscribe` / one-click `List-Unsubscribe-Post` headers (RFC 8058). The link is a hashed external access token (`/portal/u/{token}`, 400 days); the page masks the address and asks for a click (link scanners can't unsubscribe people), while mail apps' one-click POST works without a form token.
     - Unsubscribing adds a marketing-only suppression and a marketing opt-out: job email still goes out, and only the person can undo it.
     - Suppressions admin (email and texts): search, block an address (marketing only or everything), unblock staff and bounce blocks.
     - Company settings page: name, time zone, mailing address.
     - Blast emails are stored as email messages on each contact's timeline.
     - Online booking: booking types (length, start interval, gaps before/after, notice, days ahead, per-host daily cap, calendar type, asks address, creates opportunity) with hosts; weekly hours per user (two windows a day, company time zone; people set their own, `booking.manage` sets anyone's).
     - Public pages `/book/{company}` and `/book/{company}/{type}`: no JavaScript needed, rate limited, honeypot field. Open times are each host's hours minus anything on their calendar (with the gaps) and minus capped days; a booking goes to the free host with the fewest upcoming bookings. A unique slot key per host and start stops double-booking.
     - **Decided 2026-11-17:** a booking goes straight onto the host's calendar (and pings them), but contacts, properties and opportunities are only created when staff accept it in the Bookings inbox: link to an existing contact matched by email or phone, add as new, or dismiss as spam. Anonymous input never writes onto existing records, and spam never reaches the CRM. The visitor's "text me" tick box becomes a texting opt-in (source web_form) only for the number they typed.
     - Confirmation email (platform mail) with a manage link (`/portal/b/{token}`, hashed, 120 days) to see or cancel; cancelling frees the slot, cancels the appointment and notifies the host.
     - `/portal` and `/book` pages always act as an anonymous visitor, even in a signed-in staff browser.
     - Nurture workflows: ordered steps (email, text, or a task for the owner), each after a wait; everyday or marketing (marketing adds the mailing address, an unsubscribe link and needs marketing consent for texts). Enroll a contact or opportunity from its page (`nurture.enroll`); build workflows under Administration (`nurture.manage`).
     - The worker's `NurtureJob` runs due steps every minute. Each enrollment is leased with a conditional update before its step runs. Before every step it stops the enrollment if the contact replied (`StopOnReply`), the opportunity was won or lost (`StopWhenClosed`), the workflow was turned off or the contact archived. Consent is checked per message (blocked = skipped, not retried); provider failures retry 3 times with backoff. Texts wait for 8 am–8 pm company time. Emails come from the opportunity's (or contact's) owner and tasks go to them. Unsubscribing from any marketing email stops that contact's marketing workflows.
     - Every step run is recorded (done / skipped with the reason / failed) and the messages land on the record's timeline.
   - Still to do:
     - Busy calendars (ICS feeds blocking booking times). (Done since: day-before reminder emails; customers can move their booking from the manage link up to 3 times, to any open time, keeping the same host when free, with a new confirmation email.)
     - Stop on inbound email replies once mail sync exists. (Enrolling on stage entry is done through automation rules.)
     - Bounce and complaint handling (needs provider webhooks), open/click tracking (deliberately off for now), shared sender mailboxes.
10. **AI assistant** — tools with record-scope enforcement, budgets, knowledge base.
11. **Reports** (done 2026-11-17)
   - Pipeline (open opportunities by stage and owner), Sales (new leads, won, lost, win rate and won value by month, lead source and owner; lost reasons), Production (active projects and open trades by stage and trade type, backlog, sold and completed), Stale (opportunities and projects past their stage's limit, linked to the record), Tasks (open, overdue and done by person).
   - Every report runs on the same record scope as the lists, so a report never counts records the user can't see; `records.view_all` users get an owner filter. Needs `reports.view` (managers and admins by default). Date ranges are whole days in the company time zone. Won value is the signed project contract where there is one, otherwise the expected value.
   - Charts are server-rendered CSS bars (no chart library, works with the CSP). CSV export per report (UTF-8 with BOM for Excel; cells that start with = + - @ are prefixed with an apostrophe so customer-typed text can't run as a formula).
   - Still to do: saved and scheduled reports (emailed by the worker), revenue by trade/sub margin once job costing exists, marketing reports (blast and nurture results).
12. **JRS migration** — `Oes.Migrator` dry runs, review queues (duplicate properties/contacts), cutover.
13. **Mobile API hardening** (done 2026-11-17), then the mobile app.
   - Token sign-in for the app: `POST /api/v1/auth/token` (email, password, optional tenantId; a user in several companies gets 409 with the list to choose from), `POST /api/v1/auth/refresh`, `POST /api/v1/auth/signout-everywhere`. ASP.NET Core bearer tokens: access 30 minutes, refresh 30 days. Refresh re-checks the security stamp (password change or sign-out-everywhere) and active membership; every request re-checks membership, so removing someone cuts off the app immediately. Password lockout applies; sign-ins are audited.
   - One forwarding auth scheme: bearer tokens are honoured only on `/api` (a token never signs in to web pages); everything else uses the cookie. Anti-forgery is enforced for cookie-authenticated API calls and skipped for bearer calls (a cross-site page can't add an Authorization header; there is no CORS).
   - Per-user API rate limit (token bucket, `RateLimits:ApiPerMinute`, default 300) on all controllers; the rate limiter now runs after authentication. Token endpoints use the login limit.
   - Service errors become ProblemDetails for every API controller (404 not found or not visible, 403 forbidden, 422 rule broken).
   - Field-app endpoints on the same services and record scope as the web: `my-day`, `tasks` (list, create, done), `appointments` (≤ 62 days), `opportunities/{id}` (with contact phone/email), `records/{type}/{id}/timeline`, `.../notes`, `.../photos` (images/PDF, 25 MB).
   - Still to do: push notifications (`device_tokens`, APNs/FCM), offline sync and conflict handling, the app itself.
