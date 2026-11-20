# OES4 Solution Architecture (Draft v1)

**Status:** draft for review, 2026-11-16. It builds on [oes4-design-decisions.md](oes4-design-decisions.md) and [oes4-schema.md](oes4-schema.md).

**Product name: Pich** (decided 2026-11-19). The brand package is in `assets/brand/pich-v1` (draft 1: logos, colors, type; raster drafts until a vector P and outlined wordmark are approved). In the app:
- The staff app shell shows the Pich wordmark (white on the navy sidebar), the white lockup on the sign-in page, the orange badge as the favicon and app icon, "– Pich" in page titles, and Pich in system emails, help text and the AI assistant's instructions. Web-sized, transparent copies are cut from the drafts into `wwwroot/img/brand` (not redrawn).
- `wwwroot/css/pich-brand.css` maps the brand onto the existing styles: navy #062536 for the sidebar, text, buttons and links; cloud #F4F6F7 background; slate #526570 secondary text; orange #FF7900 only as an accent (current menu item, focus ring, the short rule under page titles), because white on orange and orange on white are too low-contrast for text. Status, stage and trade colors are unchanged. Inter was already the UI font.
- What contractors send their customers (proposals, change orders, material orders, emails, texts, booking pages, the portal) carries each company's own name, not Pich.
- Internal names stay: code namespaces (`Oes.*`), the database, cookies, CSS class prefixes, and the data-protection application name "OES4" (changing it would sign everyone out and make stored encrypted links unreadable).

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
  - Microsoft Edge (or Chrome) must be installed for server-side PDFs; nothing else is downloaded.
  - The file store is a folder outside `wwwroot`.
  - Nightly backups of MySQL plus the file store go off-box.
- **CI:** GitHub Actions builds, tests (including the tenant-isolation suite) and packages. Deploy is a zip/web-deploy step at first, later automated.
- **Config:** `appsettings.{env}.json` holds non-secrets. Secrets come from environment variables or user-secrets in dev. Data Protection keys are persisted to disk and protected with DPAPI on Windows.

## 9. Build order (proposed milestones)

1. **Foundation** (done 2026-11-16)
   - Solution skeleton; CI.
   - Tenancy + Identity + roles/permissions + tenant isolation tests.
   - App shell with the OES3 look; audit log; file store; SES system mail.
2. **CRM core** (done 2026-11-18)
   - Done:
     - Contacts (person/org) and properties with structures, property ↔ contact links with roles and history.
     - Duplicate-address detection by normalized key.
     - Opportunities with numbering, a pipeline board (drag and drop), stage history, won/lost with a reason, and attaching a property later.
     - Notes, documents and activity with `record_links` rollups ("This record / Everything under it").
     - Record-level visibility (own + unassigned pool unless `records.view_all`).
     - Global search and typeahead pickers, level headers and breadcrumbs, a dashboard with pipeline numbers.
     - Admin screens for pipeline stages, project/trade stages and pick lists (Administration › Lists).
     - Merging duplicate contacts and properties (done 2026-11-18, permission `records.merge`): "Merge duplicate" on the record suggests likely duplicates (same phone, email or name; same or similar address), previews what moves, and lets you swap which one is kept. One transaction moves everything pointing at the duplicate (opportunities, property links, phone numbers and emails not already there, organization links, notes, documents, activity and rollup links, tasks, appointments, emails, texts, bookings, blasts, nurture runs, claims, estimates, projects, measurements, buildings, customer links and AI chats). Blank fields on the kept record are filled; nothing is overwritten. Opting out wins: if either contact had opted out of a channel, the kept contact ends up opted out (recorded with source "merge"). Duplicate buildings get "(from …)" added. The duplicate is archived with `MergedIntoId`, and its page links to the kept record.
     - Prospecting call list and dispositions (done 2026-11-18, permission `leads.prospect`):
       - Call outcomes (`dispositions`) are edited in Administration › Pipeline & Lists › Call outcomes. Each one says when the lead comes back (call back after N days), whether it counts as a contact attempt, and can move the stage (a lost stage uses the outcome's lost reason). "Do not contact" takes the lead off the list for good and opts the contact out of marketing email and texts (source "disposition"). Every outcome must do at least one of these, so a lead never pops straight back. New companies get eight starter outcomes.
       - Pipeline stages have a "Call list" flag; new companies (and existing ones, via the migration) have New Lead and Contacted on the list.
       - Prospecting (CRM menu) filters by stage, lead source, storm and "only mine", and shows due, being worked by others and scheduled call-backs. It serves one lead at a time: never-called leads first, then due call-backs, oldest first. It shows phone (tap to call), email, address, attempts, last outcome and call history. The rep picks an outcome, with an optional note and call-back date (8 am company time), and the next lead comes up. Skip, Back (the rep's own previous calls) and Stop.
       - Locks: opening a lead locks it to the rep with a conditional update (one lead per rep; it expires after 15 minutes), so two reps never get the same homeowner, and nobody else can set an outcome while it's locked.
       - Opportunity pages log calls with an outcome too. Each outcome is kept in `opportunity_dispositions` and on the timeline.
       - The app API has `/api/v1/prospecting` (dispositions, counts, next, `{id}/disposition`, `{id}/release`).
     - Contact mailing and billing addresses (done 2026-11-18): one of each per contact, kept apart from the properties they're tied to (an owner who lives elsewhere, a property manager's office). Edited on the contact page and logged on its timeline. The `{{contact.mailing_address}}` merge field uses the mailing address, else billing, else the record's (or the contact's primary) property. Proposals show the billing (else mailing) address under "Prepared for" when it isn't the property. When contacts merge, the kept contact's addresses win and the duplicate's fill a missing kind.
     - Saved views (done 2026-11-18) on Opportunities, Contacts, Properties, Projects, Estimates, Claims and Tasks:
       - A "Views" menu saves what's on screen under a name and switches between views. A view keeps only that list's known filters (never paging), so it always shows what the viewer may see, and "mine" means the viewer. Saving with an existing name replaces it; up to 30 personal views per person.
       - Managers (`records.view_all`) can share a view with everyone and remove shared views; shared changes are audited.
       - A personal view can be your default: opening the list with no filters goes to it, and "No filters" (`?reset=1`) shows everything.
     - Lead campaigns (done 2026-11-18, permission `leads.campaigns`; Administration › Leads & Marketing › Lead Campaigns):
       - A campaign picks open leads by stage, lead source, storm, property ZIP codes, at most N attempts, created dates and "unassigned only", with a live count while editing.
       - Two kinds. A fixed list (`campaign_members`) takes the matches when it's created, and newer matches are added on request ("Add N new matching leads"); single leads can be taken out. Live filters always take whatever matches now. A campaign can't switch kinds.
       - Reps pick a campaign in Prospecting (and the app API: `GET /api/v1/campaigns`, `campaignId` on counts and next). A campaign's leads come up whatever their stage, still within the rep's own record visibility.
       - The campaign page shows leads, due now, called (with a progress bar), won, lost and still open, the call outcomes since it started, and the leads with stage, attempts, last outcome and next call.
       - OES3 campaigns were saved filters only; saved views now cover that, and campaigns add the tracking.
3. **Insurance** (done 2026-11-17)
   - Carriers: a platform default list (names only), with tenant overrides kept private and falling back to the defaults; tenants can add their own carriers and hide unused ones.
   - Claims: optional until filed, and visibility follows the opportunity; notes roll up to the opportunity, property and contact.
   - Supplements with status, amounts and dates.
   - Storm events with ZIP codes, a canvass list and storm suggestions; storm filters on the pipeline and claims.
   - Admin screens for pipeline stages and pick lists.
4. **Measurements** (done 2026-11-17; AI reading of reports 2026-11-18)
   - Default trades and measurement fields per tenant, editable under Administration › Trades & Measurements.
   - Per-property measurements page by structure × trade.
   - Versions that are never overwritten, with a "vs previous" delta, revert by making an older version current, and the report file kept on the property.
   - Roof, elevation (per wall side) and linear modes.
   - AI reading of measurement reports (done 2026-11-18): on the entry page, "Read the PDF with AI" stores the report on the property, then one structured-output call (PDF document + a schema of that trade's fields, each a number or null) proposes values. They only pre-fill the form, highlighted, with the AI's notes; a person checks and saves, and the version keeps the report and the AI's raw answer (`ExtractionJson`). Spend counts toward the AI daily budgets.
5. **Estimating** (core done 2026-11-17)
   - Done:
     - Catalog separate from many dated price lists, with a CSV importer, list copying and price editing; products.
     - Price-book and line-item estimates on one line model, with sections built from current measurements (one-click quantities) and prices copied onto lines.
     - O&P and tax (materials, all or none).
     - Approvals by exception (custom lines, below list, no price) with no self-approval.
     - Frozen versions on issue (JSON snapshot plus hash, a revision reason after v1), sign/decline, and signing wins the opportunity.
     - A printable proposal rendered from the frozen version.
     - Company estimate defaults (done 2026-11-18, Administration › Settings): terms and conditions, separate optional terms for insurance work, days an estimate stays valid, sales tax %, and insurance overhead and profit %. They're copied onto each new estimate, which can still change them; existing estimates keep theirs. Existing companies start at 30 days and 10% / 10%, as before.
     - Takeoff rules engine (done 2026-11-19), ported from OES3's structured rules with no stored formulas:
       - A rule (Administration › Takeoff Rules, `products.manage`, per trade) turns one measurement into a quantity of one product: driver × multiplier × (1 + waste%), rounded up, down or to the nearest multiple, then held between a minimum and maximum. Up to two conditions (e.g. layers ≥ 2) and an on/off option (e.g. ice & water shield, valley metal) decide when it applies.
       - Drivers are the trade's measurement fields (wall sides summed), "squares" for every sq ft field, ridges + hips and eaves + rakes for roofs, and a fixed quantity.
       - The product is fixed, or comes from a slot (shingle, ridge_cap…) the rep fills per job from products tagged with it. A product's own coverage replaces the rule's multiplier (e.g. a 33 LF ridge cap bundle). Rules can use the measurement's suggested waste.
       - New companies get starter rules for roofing (17, from OES3's set) and gutters (6), all slot-based, with notes on what each coverage assumes.
       - On an estimate section with measurements, "Takeoff" shows the drivers, slot pickers (pre-filled from the last run, or the only product in a slot), options and a priced preview, then puts the lines on the estimate. A re-run replaces only the lines the takeoff made (`EstimateLines.TakeoffRuleId`); hand-added lines stay.
     - Scope-of-work wording per takeoff rule (done 2026-11-19):
       - A rule can carry one sentence of wording with fill-ins: {qty}, {unit}, {product}, {line}, {measure} (the rule's own measurement), or any of the trade's measurements by key (e.g. {total_area_squares}, {ridge_hip}). Unknown fill-ins are refused when the rule is saved.
       - The takeoff page previews the section's scope of work: one "- " item per rule that adds a line, in rule order, with identical wording listed once. Rules still needing a product add no wording.
       - "Use it as this section's scope of work" replaces the section's scope when the lines go on. It's ticked unless the scope was typed or changed by hand since the last takeoff wrote it (`estimate_sections.TakeoffScope`), so hand wording isn't overwritten by accident.
       - New companies' starter rules come with customer-facing wording (nails, miters and end caps have none). Companies set up before this have no wording on their rules until an admin adds it.
     - Server-side PDFs (done 2026-11-19):
       - "Download PDF" on the proposal (a frozen version), work order, change order and order sheet print views (`?handler=Pdf`). Each is the page's own Razor view, run in-process into a buffer in PDF mode (`PdfPageResult`), so the PDF always matches the print view and needs no print URL or key.
       - In PDF mode the print layout inlines its styles (`css/pich-document.css`, a small Bootstrap subset, plus `oes4-print.css`), drops the scripts and flash message, and adds a CSP that allows nothing to load.
       - `IPdfRenderer` → `PlaywrightPdfRenderer`: headless Chromium through Playwright, using a browser already installed (Edge, then Chrome; or `Pdf:Channel` / `Pdf:ExecutablePath`), so nothing is downloaded. JavaScript is off, every request is aborted and the context is offline. Letter size, "Page n of m" and the document name in the footer. One browser is kept and relaunched if it dies; `Pdf:MaxConcurrent` (2) documents at a time.
       - With no browser on the server, the download goes back to the print view with the reason, and printing from the browser still works.
       - Emailing the proposal (done 2026-11-19): "Email to the customer" on the proposal print view (needs `email.send`) picks one of the opportunity contacts' addresses, with CC, BCC, a prefilled subject and message. Sending renders the PDF, saves it on the estimate (category `proposal`), emails it from the user's mailbox as an attachment (same consent rules as any email), and logs `estimate.emailed` on the estimate.
       - Saved copies (done 2026-11-19): issuing an estimate, and marking it signed, land on the proposal page with `?save=true`, which renders the PDF and saves it on the estimate once per state (`estimate_versions.PdfDocumentId`, category `proposal`; `SignedPdfDocumentId`, category `signed_proposal`, named `…-v1-signed.pdf`, with the signer's name), then reloads the page normally (so a missing browser can't loop). The versions list links "PDF as issued" and "Signed PDF". Emailing the proposal attaches the saved copy for the version's current state, rendering and saving one only if it's missing. Versions issued another way (none today) get a copy the first time they're emailed.
       - Emailing other documents (done 2026-11-19), from their print views with the same panel (`_DocumentEmail`): a change order goes to one of the customer's addresses, saved on the project (category `change_order`, logged `change_order.emailed`); an order sheet goes to a typed supplier address, saved on the trade (category `order_sheet`), and can mark the order placed with the supplier's name in the same step. An empty order sheet can't be sent. Supplier email uses `MessagingService.SendToAddressAsync`: business email to one address that isn't a contact's, filed on the record, with the same permission, visibility, opt-out, CC/BCC and attachment rules and no merge fields.
       - Work orders (done 2026-11-19): "Issue to sub" posts to the print page, which marks the order issued, renders its PDF, saves it on the work order (`work_orders.PdfDocumentId`, category `work_order`) and emails the sub the portal link with the PDF attached; a new portal link re-attaches it. With no browser on the server the email still goes out, without the attachment, and the page says why. System email (`ISystemMailer`, SES v2 simple messages) now carries attachments; the pickup mailer writes them next to the message.
       - The API serves them too (done 2026-11-19): `GET records/{type}/{id}/documents` lists a record's files with their category (proposal, signed_proposal, change_order, order_sheet, work_order, photos…), and `GET documents/{id}` downloads one, through the same visibility check as the web.
6. **Production** (done 2026-11-19)
   - Done:
     - Projects from signed estimates (one trade per estimate trade, contract = signed total).
     - Per-tenant project and trade stages with roles, which are unique per level and editable.
     - Gates by role: material date before deliveries, work date before labor, all trades complete before the project completes.
     - Forward-only automation (production start, project complete) with a move preview and "automatic" history.
     - Project and trade kanban boards (drag and drop), Remove from Board with reasons and restore.
     - Change orders with customer approval that adds to the contract (and to the trade amount).
     - Itemized change orders (done 2026-11-18): leave the amount blank on the project page and the change order opens to take items from the job's price list (when the job came from a price-book estimate), products, or custom lines. A negative quantity is a credit, prices are copied when added, and the total is the sum of the lines. Items can be edited until the customer decides; an empty change order can't be approved. Every change order has its own page and a printable copy with the contract before and after and signature lines.
     - Order sheets (done 2026-11-19): each trade has a materials order, kept apart from what was sold (the estimate sets the price; production decides what to buy).
       - Fill it from the takeoff rules, using every building's current measurements for the trade, with the same slot pickers and options as estimates (the last picks are remembered). Or copy the sold estimate's material product lines for that trade. Or add products or anything else by hand.
       - Refilling replaces only the lines that source made and nobody changed; editing a line makes it the person's.
       - A printable material order for the supplier (SKU, item, quantity, color/notes, deliver-to address, needed-by date, delivery notes). "Mark as ordered" records the supplier, notes, who and when, and can be reopened.
   - Automation rules are configurable in milestone 8 (Administration › Automation).
7. **Subcontractors** (done 2026-11-17)
   - Subs as organization contacts with a profile (trades, status).
   - Documents with expiry dates, with a cached earliest insurance expiry and badges.
   - Effective-dated rates: a new rate ends the old one.
   - Work orders on trades, with lines copied from the rates in effect on the work date.
   - Issuing emails a hashed, 60-day, revocable portal link; revise, new link and cancel all revoke it.
   - Sub portal (no sign-in, rate limited, no-referrer, noindex): accept or decline, then report completion with photos that roll up to the trade and project.
   - Work order print view (`/WorkOrders/Print/{id}`), printed from the browser or downloaded as a server-made PDF, like the proposal. Insurance-expiry alerts are done (worker `ChecksJob`).
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
     - Unmatched texts inbox (done 2026-11-19, `sms.view_all`; Communication › Unmatched Texts): texts from numbers on no contact are grouped by number with the conversation (including automatic replies), and everyone with `sms.view_all` is notified when one arrives. "File here" adds the number to a contact (as a mobile) and files the texts there, so the next text matches by itself; "New contact" creates the contact (lead source Phone In) and optionally a lead, with the texts on it; "Dismiss" hides spam or wrong numbers until they text again. Numbers on an archived contact are pointed out. Replying from the inbox (needs `sms.send` too) works only for a number that texted in and isn't on a contact yet; STOP is honored, the first reply carries the opt-out line, and filing the number later moves the replies onto the contact with the rest.
     - Appointments (done 2026-11-19): all-day events, one day or several (local midnight to midnight; the calendar shows them on every day they cover), and teammates invited besides the assignee. Invited people see it on their calendar even without `calendar.view_all`, are notified when invited, moved or cancelled, and count as busy for online booking (so do all-day events). Editing tasks and appointments in place, due/overdue, stale and expiry alerts, and automation rules were done earlier.
     - Email CC, BCC and attachments (done 2026-11-19): any email from a record can carry CC and BCC (up to 10 each, plain addresses; one suppressed for everything, or for marketing on a marketing email, stops the send) and up to 10 attached documents (20 MB total), each opened through the same visibility check as downloading it. Sent attachments are recorded (`email_attachments`, pointing at the stored document) and shown with paperclip links on the timeline and the message page, which also lists CC/BCC. The record email form lists the record's files (including those under it, such as an opportunity's estimates; only ones the user can open; newest 30) to tick as attachments. The development mail provider writes attachments next to the email in the pickup folder.
   - Still to do (needs external credentials or later work):
     - Real providers: Microsoft Graph mail (per-user OAuth, inbound sync and threading) and calendar sync, Twilio texting (request-signature validation, A2P registration, delivery status callbacks). SignalR live updates, email/push delivery of notifications and preferences.
     - MMS.
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
     - Busy calendars (done 2026-11-19; My hours › Busy calendars): anyone can add up to 5 outside calendars (Google, Outlook, iCloud) as .ics feed links; `booking.manage` can manage anyone's. Only busy times are kept (never titles), from yesterday to 90 days out, and they block online booking like appointments do. Links are encrypted at rest (ASP.NET data protection, one key ring for the web app and the worker) and fetched through `SafeHttpFetcher`: https on 443 only, every resolved address must be public and the connection is pinned to it, same-host redirects only, 2 MB and 15 s caps. A bad link is refused when added; a failed refresh keeps the last busy times and shows why. The worker's `BusyCalendarJob` re-reads feeds every 15 minutes. The .ics reader handles UTC, TZID and floating times, all-day dates, DTEND or DURATION, free and cancelled events, and daily, weekly (BYDAY), monthly and yearly repeats with INTERVAL, COUNT, UNTIL and EXDATE. Busy times can show (greyed, no details) on the OES calendar. Day-before reminders and customer rescheduling were done earlier.
   - Still to do:
     - Stop on inbound email replies once mail sync exists. (Enrolling on stage entry is done through automation rules.)
     - Bounce and complaint handling (needs provider webhooks), open/click tracking (deliberately off for now), shared sender mailboxes.
10. **AI assistant** (assistant done 2026-11-18; needs an API key to switch on)
   - **Turning it on:** set `Ai:Provider` to `Anthropic` and `Ai:ApiKey` (development: `dotnet user-secrets set "Ai:ApiKey" "<key>" --project src/Oes.Web`; production: a protected setting or the `ANTHROPIC_API_KEY` environment variable, never in a committed file). Until then the Assistant page says it isn't connected and nothing calls out.
   - Claude through the official Anthropic SDK (`Anthropic` NuGet) behind `IAiClient`: model `claude-opus-5-5` (setting `Ai:Model`), adaptive thinking (always on for this model) with `Ai:Effort` (default `medium`), automatic prompt caching, and the server-side refusal fallback (`fallbacks: default`, beta `server-side-fallback-2026-07-01`) so a safety decline is retried on another model inside the same call.
   - We host the tool loop. Tools: `search_records`, `search_knowledge`, `get_record` (opportunity, contact, property, project, with open tasks and recent timeline), `list_my_tasks`, `my_schedule`, `pipeline_summary`, `create_task`. Every tool calls the same service as the web pages as the signed-in user, so record scope and permissions apply to each call; a hidden record comes back as "not found". The assistant can't send messages, move stages or edit records. Tool output is marked as data, and model output is always HTML-encoded on the page.
   - The transcript is stored append-only, blocks exactly as received (thinking signatures included), so each turn replays the history unchanged. Every tool call is logged with its input and outcome (`ai_tool_calls`).
   - Budgets: per-user and per-company daily spend (`Ai:DailyUserBudgetUsd`, default $3; `Ai:DailyTenantBudgetUsd`, default $30), computed from token usage at list price and checked before every model call inside the loop; at most `Ai:MaxToolRounds` (8) lookups per message. AI Usage page (`ai.manage`) shows spend by person and day.
   - "Ask the assistant about this" on opportunity, contact, property and project pages starts a conversation about that record.
   - **Carrier scope import** (done 2026-11-18): "Import carrier scope" on an unsigned estimate uploads the carrier's PDF (kept on the estimate as `carrier_scope`), and one structured-output call lists its line items (category, code, activity, description, quantity, unit, unit price) plus carrier, price-list label and notes. Lines are matched to the estimate's price list in OES3's order (`ScopeMatcher`, fuzzy matching added 2026-11-19): item code + category + activity; the same wording (an opening "Remove", "R&R", "Install"… read as the activity; no activity read as Replace); the same item at another activity; our description cut off (45+ characters that start the carrier's); then shared words (Jaccard). 80%+ shared words is taken; 50–80% is only a suggestion the reviewer can accept per line. A line whose unit differs from ours is never taken by itself (it becomes a suggestion with a note). Matches by wording, another activity or a cut-off description are marked to check on the review page. A review page shows each line with the carrier price next to ours; the user unticks lines, fixes quantities and adds them as a new section. Matched lines use our price list; unmatched ones become custom lines at the carrier's price, so they go through approval by exception. Imports are kept (`scope_imports`: review, imported or discarded) with the raw AI answer for audit, and count toward the AI budget. Area assignment is not ported yet.
   - **Supplement compare** (done 2026-11-18): from any scope import, a printable "Supplement" page sets the carrier scope against the estimate as it is now, item by item (pairing on category + item code + activity; custom lines made from a scope pair back through the code in their description; uncoded lines by description). Each item shows carrier and our quantity and unit price and the difference; items only on one side are flagged; totals are before O&P and tax. Differences are listed by default, with a toggle for matching items. Reading a scope and comparing also work on signed estimates (supplements usually come after signing); only adding lines needs an unsigned estimate. The estimate page button is "Carrier scope & supplement".
   - **Knowledge base** (done 2026-11-19): Administration › AI Knowledge Base (`ai.manage`) holds the company's own articles (title, category, plain text or Markdown up to 100,000 characters, on/off), typed, pasted or filled from a file: .txt or .md, or a PDF whose text is read locally with PdfPig (no AI call; added 2026-11-19; a scanned PDF has no text layer and is refused with a note). Saving cuts an article into passages of about 1,200 characters along paragraphs, starting a new one at each heading (`# …` or a short line ending in ":"), stored in `knowledge_chunks` with a lowercase word copy for matching.
     - The assistant's `search_knowledge` tool (and the page's "Test search" box) narrows to passages sharing a query word in SQL, then ranks them in memory: rarer words count more, title words get a bonus, plurals are cut back to a stem, at most two passages per article, top five. No embeddings or outside index.
     - Articles are company-wide: anyone with `ai.assistant` can search every active article. The prompt tells the model to search before answering policy questions, name the article, and say so when nothing matches.
   - **Streaming replies** (done 2026-11-19): with script on, the chat posts to `/Assistant?handler=Stream` and the answer arrives as server-sent events (`start`, `text` pieces, `thinking`, `tool` with a label like "Checking the knowledge base", then `done` or `error`); the page draws it with `textContent` only and reloads to the saved conversation at the end. Without script the form posts as before.
     - `IAiClient.CreateStreamingAsync` reports text as it's written and returns the same `AiResponse` as `CreateAsync` (a default implementation reports whole blocks, so the scripted test client streams too). The Anthropic client streams the beta Messages call and rebuilds the message with `StreamAccumulator`: text and thinking from their deltas (signature kept), tool input from its JSON pieces, stop reason and usage from `message_start`/`message_delta`.
     - Server-side fallbacks: a `fallback` block is now kept in the transcript (`AiFallback`, stored and sent back as-is), and after a mid-stream fallback the declined model's thinking and tool calls before the boundary aren't sent back; its text is.
     - Fixed while testing: the SDK's `ToString()` on the stop reason and model keeps the JSON quotes (`"tool_use"`), so the live assistant would have stopped after its first reply without running any lookup, and refusal and cut-off checks never matched. `WireName`/`ModelName` now strip them (unit-tested against the SDK's own types).
   - Still to do: per-tenant API keys. (AI reading of measurement reports is done; see milestone 4.)
11. **Reports** (done 2026-11-17)
   - Pipeline (open opportunities by stage and owner), Sales (new leads, won, lost, win rate and won value by month, lead source and owner; lost reasons), Production (active projects and open trades by stage and trade type, backlog, sold and completed), Stale (opportunities and projects past their stage's limit, linked to the record), Tasks (open, overdue and done by person).
   - Every report runs on the same record scope as the lists, so a report never counts records the user can't see; `records.view_all` users get an owner filter. Needs `reports.view` (managers and admins by default). Date ranges are whole days in the company time zone. Won value is the signed project contract where there is one, otherwise the expected value.
   - Charts are server-rendered CSS bars (no chart library, works with the CSP). CSV export per report (UTF-8 with BOM for Excel; cells that start with = + - @ are prefixed with an apostrophe so customer-typed text can't run as a formula).
   - Marketing and prospecting reports (done 2026-11-18):
     - Marketing: email blasts started in the range with sent, failed, skipped, unsubscribes and new leads from recipients within 30 days (and how many were won); nurture workflows with enrollments started in the range, still running, finished, stopped and opportunities won; and why runs stopped.
     - Prospecting calls: calls by outcome (with share) and by person (calls, leads called, moved forward, do not contact). Lead and call counts stay within the user's record scope.
   - Saved and scheduled reports (done 2026-11-19): "Save this report" keeps a report with its owner filter and a rolling range (this month, last month, this year, last 7/30/90 days) or fixed dates, under "My saved reports". It can be emailed to its owner weekly (a weekday) or monthly (day 1–28) at 7 am company time, or right away. The worker's `ReportsJob` (every 15 minutes) runs each due report signed in as its owner for that one scope, so permissions and record visibility are checked exactly as on the page; an owner who leaves or loses `reports.view` stops getting it. Up to 20 per person; the same name replaces.
   - Still to do: revenue by trade/sub margin once job costing exists.
12. **JRS migration** — `Oes.Migrator` dry runs, review queues (duplicate properties/contacts), cutover.
13. **Mobile API hardening** (done 2026-11-17), then the mobile app.
   - Token sign-in for the app: `POST /api/v1/auth/token` (email, password, optional tenantId; a user in several companies gets 409 with the list to choose from), `POST /api/v1/auth/refresh`, `POST /api/v1/auth/signout-everywhere`. ASP.NET Core bearer tokens: access 30 minutes, refresh 30 days. Refresh re-checks the security stamp (password change or sign-out-everywhere) and active membership; every request re-checks membership, so removing someone cuts off the app immediately. Password lockout applies; sign-ins are audited.
   - One forwarding auth scheme: bearer tokens are honoured only on `/api` (a token never signs in to web pages); everything else uses the cookie. Anti-forgery is enforced for cookie-authenticated API calls and skipped for bearer calls (a cross-site page can't add an Authorization header; there is no CORS).
   - Per-user API rate limit (token bucket, `RateLimits:ApiPerMinute`, default 300) on all controllers; the rate limiter now runs after authentication. Token endpoints use the login limit.
   - Service errors become ProblemDetails for every API controller (404 not found or not visible, 403 forbidden, 422 rule broken).
   - Field-app endpoints on the same services and record scope as the web: `my-day`, `tasks` (list, create, done), `appointments` (≤ 62 days), `opportunities/{id}` (with contact phone/email), `records/{type}/{id}/timeline`, `.../notes`, `.../photos` (images/PDF, 25 MB), `.../documents` (the record's files, newest first, only ones the user can open) and `documents/{id}` (download).
   - Still to do: push notifications (`device_tokens`, APNs/FCM), offline sync and conflict handling, the app itself.
