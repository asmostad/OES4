# OES3 → OES4 Feature Inventory

> **Sources.**
> - Sections 1–12 come from a full read of the March 2026 copy (`C:\Dev\OES3`). The live features that copy did not have are in **section 13**.
> - **Section 13** comes from a re-audit of the **live** code at `J:\My Drive\Sites\JohnsonRoofers.com\OES3` (changed through 2026-11-12), done 2026-11-16.
> - Where the live code changed an older feature, the section 13 entry takes precedence.
> - Data-model and estimating decisions are in [oes4-design-decisions.md](oes4-design-decisions.md).

**Source:** a full code read of `C:\Dev\OES3` (about 74k lines: every page, API endpoint, service, include, migration and the shared JS/CSS), done 2026-11-15. This replaces the first draft, which was mostly based on OES3's CLAUDE.md.

**Companion docs:**
- [oes3-known-issues.md](oes3-known-issues.md): bugs, security holes and JRS hard-coding we should *not* carry forward.
- [oes3-reference-data.md](oes3-reference-data.md): permission keys, settings, lookup/seed data, merge fields and automation triggers.

**Status key:**
- **Port**: bring over and keep the behavior.
- **Rework**: bring over but redesign it (for multi-tenancy, or because the OES3 version is broken or half-built).
- **Drop**: not in OES4 (decided).
- **Later**: deferred to a post-launch phase.
- **Decide**: still open.
- **New**: doesn't exist in OES3.

---

## Platform decisions

| Decision | Choice | Status |
|---|---|---|
| Hosting | Existing Windows Server / IIS VPS (Hostek) | Decided 2026-11-15, for the time being |
| Language / framework | ASP.NET Core (C#), Razor Pages, EF Core with global tenant query filters | Recommended. Needs a final yes. |
| Database | MySQL 9.6 (same as OES3) | Proposed |
| UI | Keep the OES3 look: Bootstrap 5.3.3, Bootstrap Icons, Inter, the `oes3.css` tokens and components | Proposed |
| Mobile apps (iOS + Android) | After the multi-tenant web launch, covering most of the web app | Requirement |
| API | API-first: a versioned REST API (`/api/v1`) over a shared service layer, used by both Razor Pages and mobile | Proposed |
| Auth | ASP.NET Core Identity. Cookies for web; OAuth2/OIDC tokens (OpenIddict) for mobile | Proposed |
| Mobile framework | React Native (Expo), .NET MAUI or Flutter | Decide later |

### Scope decisions (2026-11-15)

- **Drop:** collections (deposit batch entry, ledger, AR aging), the commission engine (schedules, tiers, team overrides, chargebacks, monthly reports, CSV export, hire-date rule), weather monitoring, and the document template builder.
- **Keep:** project-level financials (line-item cost/price/margin, invoice amount, job cost, gross margin).
- **Later:** all third-party integrations (CompanyCam, FreePBX call reports, RoofHub/SRS, QuickBooks, HailRecon/IHM, the email lead monitor, Fixr, RE.ai). The OES4 design should leave room for them.
- **Documents:** port the proposal print now. Defer every other system-generated document.
- **Data model:** the property is the root, and one contacts table replaces consumers/companies. See [oes4-design-decisions.md](oes4-design-decisions.md). Sections 3 and 4 below describe OES3's consumer/company screens; in OES4 their features carry over onto contacts, organizations and properties.
- **Estimating:** two methods (price book and line items), with many dated price lists per tenant. See the design doc.
- **Insurance carriers:** a platform default list, plus a per-tenant editable copy.

### What the mobile requirement adds to the backend

- Tenant lookup at login by email (mobile can't use a subdomain). A tenant picker if a user belongs to more than one.
- Push notifications (APNs/FCM) as a third channel beside in-app and email. Needs a device token table.
- Camera photo and file uploads: chunked, retryable, sent to per-tenant storage.
- Offline or poor signal in the field for selected screens: today's appointments, tasks, project/trade details, notes, photos.
- Maps and directions to site addresses.
- SignalR for notifications and lead lock heartbeats (replaces OES3's 60-second polling).
- API rules from day one: versioning, paging, a consistent error format, rate limits, and server-side permission checks.

---

## 1. Platform, access and app shell

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 1.1 | Login / logout | Username + password, 2-hour session, redirect to the originally requested page, `lastLoginAt` | Rework | Users belong to tenants. Login by email. Add lockout and rate limiting. |
| 1.2 | "Remember me" | The checkbox exists but **does nothing** | Decide | Implement properly or drop it. Mobile gets refresh tokens either way. |
| 1.3 | Password recovery / reset | **Placeholder pages only.** Nothing is ever sent. | Rework | Required for SaaS. Use an emailed token link. |
| 1.4 | Password storage | Unsalted SHA-384 (the functions are misleadingly named "Bcrypt") | Rework | ASP.NET Identity hashing. JRS users reset on first login after migration. |
| 1.5 | Change password / admin reset | Minimum 8 characters, no other rules | Port | Add "must change at next login" after an admin reset. |
| 1.6 | Roles | 4 fixed roles (admin, sales_mgr, production_mgr, rep) with **no UI**; set by SQL | Rework | Per-tenant role editor, with the 4 roles as seeded defaults. |
| 1.7 | Permissions | 53 keys. Admin bypasses every check. 10 keys are seeded but never checked. Several screens test role *names* instead of permissions. | Rework | One permission model, checked server-side everywhere. No role-name checks. Take the cleaned-up key list from the reference doc. |
| 1.8 | Rep data scoping | Reps see their own records plus unassigned leads/prospects. Customers and projects are own-only. "Manager" = hard-coded role list. | Port | Base it on a permission (e.g. `records.view_all`), not role names. Also enforce it on detail pages and search (OES3 doesn't). |
| 1.9 | User admin | List, show inactive, edit (name, username, email, phone, MS365 mailbox, PBX ext, role, active), reset password, deactivate/reactivate | Port | Add invites by email. Prevent self-deactivation and unauthorized admin grants. Drop hire date (commissions only). |
| 1.10 | Profile | Edit name/email/phone; account info; email on/off per notification type | Port | Fix the bug where saving wipes mailbox/PBX fields. Add avatar upload (the field exists in OES3 but is never set). |
| 1.11 | System settings page | Groups: company, app, leads, nurture, twilio, ms365, pbx, companycam, roofhub, ihm. Typed fields (text, number, switch, password, select, JSON). Reloads on save. | Rework | Split into platform settings (super-admin) and tenant settings. Encrypt secrets and never render them back into HTML. |
| 1.12 | Dev/prod toggle | A DB setting | Rework | Use environment config instead. |
| 1.13 | Sidebar navigation | Main / CRM / Production / Finance / Communication / Reports / Resources / Administration, gated by permission (with gaps) | Port | Same structure minus Finance. Gate everything consistently. Tenant logo replaces the JRS logo. |
| 1.14 | Topbar | Sidebar toggle, global search (hidden on mobile), "New" quick-add (Consumer, Company, Project, Estimate, Task, Appointment), notification bell, user menu | Port | Permission-gate quick-add. Make search usable on small screens. |
| 1.15 | Flash messages, toasts, confirm dialogs, clickable rows, phone masking, currency formatting, drag-drop upload zones, pagination component | Shared JS/CSS helpers | Port | Keep the look; encode all output. |
| 1.16 | Error page | Logged; shows a timestamp "Error ID" that isn't actually logged | Rework | Use real correlation IDs and structured logging. |
| 1.17 | **Tenant management** | n/a | New | Create tenant, onboarding wizard, seed default lookups, plan/limits, suspend. Billing optional. |
| 1.18 | **Platform super-admin** | n/a | New | Manage tenants, support "log in as", global health. |
| 1.19 | **Tenant branding** | Hard-coded JRS logo, name and footer | New | Logo, company name, colors, footer per tenant, applied to the app, emails and PDFs. |
| 1.20 | **Audit log** | Only `activity_log` on records; no audit of admin/security actions | New | Log user, role, permission and settings changes. |

## 2. Dashboard and search

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 2.1 | Stat cards | My Leads / Prospects / Customers (consumer and company counts side by side, each linked to a filtered list), My Projects, My Estimates (total, draft, sent) | Port | Managers only see their own numbers in OES3. Add a team view. |
| 2.2 | Recent activity | Last 10 activity rows (everyone's for managers, own for reps) | Port | |
| 2.3 | My Tasks widget | Overdue / Today / Pending counts plus 5 upcoming with due and priority badges | Port | |
| 2.4 | My Appointments widget | Next 7 days, max 5, type badge, entity link | Port | |
| 2.5 | Recent notes | Last 5, truncated to 120 characters, links to the record's Notes tab | Port | |
| 2.6 | Global search typeahead | 2+ characters, 300 ms debounce, top 5 each of Consumers / Companies / Projects / Estimates, Enter for the full page, Escape to close | Port | Scope results to what the user may see (OES3 shows everything). Add arrow-key navigation. |
| 2.7 | Search results page | Top 10 per type with "View all N" links to the filtered list pages | Port | |

## 3. CRM: consumers, companies, contacts

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 3.1 | Consumer list | Search (name, email, phone, mobile, city, zip), Stage / Owner (managers) / Source filters, sortable columns, 25 per page, owner or "Unassigned" | Port | Add bulk actions (assign, stage change, enroll); OES3 has none. |
| 3.2 | Company list | Same, plus a SIC filter and contact count column | Port | |
| 3.3 | Saved filters | Per user per list (consumers, companies, projects, estimates), set default (auto-applied), load/delete | Port | Also save sort order; add rename/update. |
| 3.4 | Consumer form | First/last (required), email, phone, mobile, address, owner (defaults to self), lead source (free text plus a hard-coded roofing list) | Port | Make lead sources a per-tenant lookup. Add duplicate warning on manual create (OES3 has none). |
| 3.5 | Company form | Name, email, phone, fax, website, address, SIC code/description, industry, owner, lead source | Port | |
| 3.6 | Company contacts | Inline add/edit/remove: first/last, title, email, phone, mobile, primary (only one per company) | Port | Add permission checks (OES3 has none). Let contacts receive SMS. |
| 3.7 | Lifecycle stages | Lead → Prospect → Customer. Buttons can move forward or back. Unassigned records auto-assign to whoever changes the stage. | Port | Stage IDs 1/2/3 are hard-coded. Use stage "roles" so tenants can rename them. |
| 3.8 | Record detail page | Tabs: Overview, Notes, Documents, Email, SMS, Tasks, Appointments, Nurture, Activity (plus Photos and Weather, both out of scope). Header: Edit, New Estimate, New Project (Customers only). | Port | This is the core layout to keep. |
| 3.9 | Rollup ("All / This Record") | Notes, docs, email, SMS, tasks, appointments and activity roll up from the consumer/company through all its projects and trades, with origin badges | Port | A defining feature. Fix the duplicate email rows. |
| 3.10 | Notes | Add (no edit), delete by author or admin. Timeline. | Port | Add edit. Maybe @mentions. |
| 3.11 | Documents / attachments | Drag-drop upload, category, list with icon/size/uploader, delete by uploader or admin | Rework | Per-tenant private storage, file-type allow-list, size limit, authorized downloads (OES3 serves them publicly). |
| 3.12 | Activity log tab | Last 50, rolled up | Port | |
| 3.13 | Soft delete | Functions exist but **no UI** to deactivate consumers or companies | Decide | Add archive/restore? |

## 4. Leads and prospecting

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 4.1 | Dispositions | Call-outcome dropdown on Leads. Saves disposition, increments attempts, sets recycle date. "Converts" dispositions move the record to Prospect, assign it, and set a default prospect status. | Port | Per-tenant lookup. Enforce ownership and locks on the server. |
| 4.2 | Lead recycling | `recycleDate` is written **but never used**; no recycle queue | Decide | Build a recycle queue, or drop the field. |
| 4.3 | Prospect statuses | 8 pipeline statuses, each with a stale threshold. Shows "N hrs ago" and a Stale badge. | Port | Per-tenant lookup. Wire up the stale notification (never built in OES3). |
| 4.4 | Back / Next prospecting navigation | Walks the filtered list you came from (filters carried in the URL as Base64), skipping leads that are owned or locked by others | Port | Show "position N of M" (returned by the API but never displayed). |
| 4.5 | Concurrency lock | Lock on open, 2-minute heartbeat, release on page unload, 5-minute timeout. "In use by X" banner. | Rework | Make the lock atomic and use SignalR. |
| 4.6 | CSV lead import wizard | Upload (10 MB), column mapping with auto-map aliases and samples, duplicate preview (email / phone / name+zip / normalized address+zip, configurable), skip-duplicates toggle, execute. Company imports can create a primary contact. Import history. | Port | Detect duplicates within the file. Support multi-line quoted fields. Optionally assign an owner or round-robin. |
| 4.7 | Lead campaigns | Saved shared filters ("lead banks") per entity type with a live preview count; shown as a dropdown on the list pages | Rework | Today a campaign is just a filter with no membership or progress tracking. Decide whether campaigns should become real lists with progress. |
| 4.8 | Round-robin assignment pool | One "default" pool of users with an order and a last-assigned pointer | Port | Per tenant, concurrency-safe. Also usable by imports and webhooks. |
| 4.9 | Email lead monitor | Scans a shared M365 mailbox. Per-source sender match and subject filter, regex field extraction, test-parse tool, duplicate check, round-robin assign, notification, log viewer. | Later | Treat as an integration (owner, 2026-11-15). |
| 4.10 | IHM webhook (Interactive Hail Maps via Zapier) | Public endpoint that creates or updates consumers from hail-map leads | Later | Treat as an integration. Could become a generic per-tenant "inbound lead webhook". |

## 5. Projects, estimates and production

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 5.1 | Project list | Search (name, claim #, city, zip, client), type / stage / owner filters, saved filters, sortable, expandable trade sub-rows (stage, days-in-stage, item count, price) | Port | |
| 5.2 | Project form | Name, type, client (consumer **or** company, set at create only), site address, owner, insurance card (shown for the Insurance type) | Port | Allow changing the client. Base "is insurance" on a type flag, not ID 1. |
| 5.3 | Insurance fields | Carrier (lookup or "Other"), claim #, policy #, date of loss, deductible, adjuster name/phone/email | Port | |
| 5.4 | Insurance companies admin | Carrier list (15 seeded): phones, fax, emails, claims email/fax, preferred correspondence, address, notes | Port | Decide: one global list shared by all tenants, or per tenant. |
| 5.5 | Supplement tracking | Status (none/needed/sent/approved/denied), sent date, response date, notes; insurance projects only | Port | |
| 5.6 | Project detail page | Tabs: Overview, Trades & Line Items, Notes, History, Documents, Email, SMS, Tasks, Appointments, Financials (plus Photos, deferred) | Port | |
| 5.7 | Two-level workflow | Project stages (Verification → In Production → Waiting for Invoice → Waiting for Payment → Paid → Closed) and trade stages (Trade Setup → Material Orders → Material Deliveries → Work In Progress → Final Walkthrough → Completed). One step at a time; workflow logs. | Rework | Stage **names** are hard-coded all over OES3. Make stages per-tenant and give them "roles" (initial, production start, materials, labor, done) so the rules key off roles. |
| 5.8 | Stage gates | Material Deliveries requires a material date. Work In Progress requires a work date **and** a subcontractor. | Port | Make the gates configurable per stage. |
| 5.9 | Start Production | Trade moves from Setup to Material Orders. The project auto-moves from Verification to In Production. | Port | |
| 5.10 | Auto-advance to invoice | When every trade is complete, the project jumps to Waiting for Invoice and the closing documents are generated | Port | |
| 5.11 | Days-in-stage badges | Green up to 3 days, yellow up to 7, red beyond | Port | Make the thresholds configurable. |
| 5.12 | Workflow kanban boards | **Production board** (trades by stage, trade/owner filter) and **Project board**. SortableJS drag-drop with a confirm modal that previews which automations will fire. Complete/Close buttons. Recently completed tables. | Port | Drag-drop **does** work in OES3; the docs were wrong. Also usable on touch/mobile. |
| 5.13 | Trades on a project | Add a trade category (no duplicates), remove, per-trade collapsible cards with stage pips | Port | |
| 5.14 | Trade templates | Adding a trade auto-inserts default line items from `trade_templates`. **There is no admin UI**; templates are SQL only. | Rework | Build a template editor. |
| 5.15 | Trade detail page | Overview (description "used in proposals", scheduling: material date, work date, subcontractor), Line Items, Documents, Notes, Email, SMS, Tasks, Appointments, History. Production lock banner. | Port | |
| 5.16 | Line items | Types: material, labor, sales tax, equipment, fee, discount. Product picker filtered by trade auto-fills unit, cost, price. Color/size options. Qty × cost/price totals. | Port | Fix color/size (they are never saved in OES3). Allow reordering. Decide whether to add a real tax calculation (OES3 has only manual tax lines). |
| 5.17 | Totals and margin | Trade and project totals (material, labor, cost, price, gross profit, margin %) | Port | |
| 5.18 | Apply Margin tool | Re-prices every line as cost ÷ (1 − margin %) | Rework | Skip zero-cost, tax and discount lines (OES3 breaks them). |
| 5.19 | Estimates | A project flagged as an estimate. Number `EST-0001`. Statuses: draft, sent, accepted, declined, expired, revised. Expiration date, internal notes, status timestamps. | Port | Make numbering safe and per tenant. Auto-expire. Keep "estimate" and "project" as one record type, or split them? |
| 5.20 | Estimate list | Stat cards (draft/sent/accepted/declined/active value), filters, saved filters | Port | |
| 5.21 | Convert estimate to project | Flips the record to a project in the initial stage; trades go to Trade Setup; sends the welcome email | Port | Require Accepted? Make it transactional. |
| 5.22 | Duplicate estimate | Copies header, trades and lines as "X (Copy)" and marks the original Revised | Port | |
| 5.23 | Project financials tab (kept) | Invoice amount and job cost (manual entry), gross margin %, pass/fail badge against a 30% threshold | Port | Make the threshold a tenant setting. "Open balance" depended on collections, so drop it or replace it with a manual "amount paid" field? |
| 5.24 | Product catalog | Name, description, SKU, RoofHub SKU, unit (14 options), line type, default cost/price, color/size options, trade mapping, active | Port | Per tenant. Maybe a starter catalog. |
| 5.25 | Product CSV import | Choose a trade, upload, skip existing names | Rework | The parser breaks on quoted fields. |
| 5.26 | Trade categories admin | Name, order, description, active, project count | Port | Add color/icon. |
| 5.27 | Subcontractors | Company, contact, phone, email, specialty, address, notes, photo. Documents (insurance, license, agreement, W-9, other) with expiration dates shown red or amber. | Port | Add expiring-insurance alerts, and optionally block scheduling a sub whose insurance has expired. |

## 6. System-generated documents (PDF)

**Decided (2026-11-15):** port the **Proposal** now. Every other generated document is **Later**. The live OES3 rewrote most of these documents and added more (see 13.9). The template **builder** is dropped.

| # | Document | Level | Generated by | Status | Notes |
|---|---|---|---|---|---|
| 6.1 | Proposal | Estimate | "Generate Proposal" button | Decide (likely Port) | Cover page, scope and pricing per trade, total, notes, **9 hard-coded terms** (JRS-specific, Nebraska late-fee statute), signature blocks. Terms must become tenant-editable. |
| 6.2 | Materials List | Trade | Button, and automatically on Material Deliveries (emailed to the customer) | Decide | Despite the name it shows **all** lines with customer pricing. Likely needs a no-price version. |
| 6.3 | Statement of Work / Depreciation Release | Project | Button, and automatically when all trades are complete | Decide | Letter to the insurance carrier. The completion-date bug means it always shows today's date. |
| 6.4 | Certificate of Completion | Trade | Button, and automatically at Final Walkthrough (plus a "get it signed" task) | Decide | |
| 6.5 | Final Invoice Letter | Project | Automatically when all trades are complete | Decide | Total due ignores payments and deductible. |
| 6.6 | Impact Rating Letter and Certificate | Project (insurance) | Automatically when all trades are complete | Decide | Roofing-specific wording; the license # line is blank. |

**Rendering:** OES3 uses headless Chrome against print pages, protected by a shared secret key in the URL, plus `cfdocument`. OES4 should use one server-side PDF engine with tenant branding and versioned filenames.

## 7. Communication

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 7.1 | Send email (MS 365 Graph) | Compose modal: from = the user's M365 mailbox; To dropdown of the record's emails; CC; template picker; TinyMCE body; attach company docs or record docs. Logged to the record. | Rework | Each tenant connects its own M365 tenant (OAuth admin consent). Add BCC, reply/threading and local-file attachments. Decide whether to also offer a platform-sent email option (no M365 required). |
| 7.2 | Mailbox sync and matching | Graph delta sync per user; matches addresses to consumers, contacts and companies plus their projects; unmatched addresses are listed for admins | Rework | Fix the large-mailbox loop and UTC times. One row per message, linked to many records. |
| 7.3 | Email hub page | Search, direction and date filters; detail modal; admin sync status, sync-all and reset | Port | Add a "New Email" button (compose is unreachable from the hub today). |
| 7.4 | Email templates | Name, subject, rich body, active; click-to-insert merge fields | Port | It's the *template builder for PDFs* that is dropped, not email templates. |
| 7.5 | Send/receive SMS (Twilio) | Compose with a 1600-character limit and segment counter; inbound webhook matches the phone number to records; delivery status tracking | Rework | Per-tenant number or subaccount; route webhooks by the "To" number. Enforce signature validation. Add STOP/opt-out handling, an inbound-SMS notification, and A2P 10DLC registration. |
| 7.6 | SMS hub page | Search and filters, status badges, admin webhook stats | Port | |
| 7.7 | SMS templates | Live character counter, merge fields | Port | Add a template picker to manual compose (OES3 only uses SMS templates in automations). |
| 7.8 | Merge fields | About 60 fields (consumer, company, customer, contact, project, insurance, trade, crew, user, global). Unresolved tags are stripped. | Port | Single resolver for email, SMS and PDFs (OES3 has three copies that differ). |

## 8. Tasks, calendar and notifications

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 8.1 | Tasks | Title, description, due date/time, priority (low/normal/high/urgent), assignee; attached to any record or "General"; complete/reopen/cancel | Port | Fix sort-by-priority (sorted alphabetically). Notify when the assignee changes on edit. |
| 8.2 | Tasks page | Stat cards (pending/overdue/today/this week), filters, manager view of all tasks, checkbox complete | Port | |
| 8.3 | Calendar | FullCalendar month/week/day. Shows tasks (15-minute blocks), appointments and milestones (date of loss, supplement sent, material date, work date). User filter. Click-to-create, drag/resize. | Port | Fix the timezone and all-day bugs. Per-tenant/user time zone (OES3 hard-codes Central). |
| 8.4 | Appointments | 10 types, assignee, attendees (name and email chips), location (pre-filled from the site address), all-day; complete/cancel | Port | Notify on reschedule/cancel. Add reminders and double-booking warnings. |
| 8.5 | M365 calendar two-way sync | Pull (delta) and push (create/patch/delete), attendees become invites | Rework | Per-tenant OAuth. Fix the cancel-not-deleted bug, terminal conflict state, and attendee duplicates. |
| 8.6 | In-app notifications | Bell with unread count (60-second polling), dropdown of latest 15, mark read / mark all read | Port | Use SignalR, and add push for mobile. |
| 8.7 | Notification emails | Per-type email opt-in. Types: task assigned/overdue/due soon, appointment assigned/invited, lead assigned, workflow. | Port | Fix endless overdue repeats. Don't log staff notifications into customer email history. |

## 9. Automation

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 9.1 | Nurture (drip) workflows | Steps: send email, send SMS, create task, wait, each with a day/hour delay. Activate/deactivate (pauses enrollments). Enroll from a record; pause/resume/cancel. Runs every 15 minutes. | Port | Fix: first-step delay ignored, failures still advance, resume skips the delay. Add opt-out/DNC checks, stop on reply/conversion, bulk enroll UI, step log viewer. |
| 9.2 | Workflow automation triggers | Project created → welcome email. Material Orders → email plus notify production managers. Material Deliveries → materials list PDF, email, task. Work In Progress → email. Work Completed → email. Final Walkthrough → certificate plus task. All trades complete → closing docs plus "create invoice" task. Labor scheduled → email plus SMS. Per-trigger on/off and template/task config. | Rework | Tie triggers to stage roles, not names. Only 3 of the 8 triggers actually honor the task config. Prevent re-sending when a stage is re-entered. Consider a general "when stage X → do Y" rule builder. |
| 9.3 | Internal notification toggles | Turn each notification type on/off for the whole company | Port | |

## 10. Reports

| # | Report tab | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 10.1 | Lead Activity | Leads created, touched, converted; table by rep | Port | Include companies (OES3 counts consumers only). |
| 10.2 | Conversion | Projects created, reached Paid, close rate, average days to close; by rep | Port | |
| 10.3 | Pipeline | Total open $ and a weighted forecast (stage weights hard-coded); by stage | Port | Stage weights become a stage setting. Exclude Closed. |
| 10.4 | Revenue by Stage | Invoice $, collected $, balance by stage | Rework | Drop the collected and balance columns (collections dropped). |
| 10.5 | Production | Trades by category: projects, in production, completed, average days | Port | |
| 10.6 | Stale Projects | Open projects not updated in N days | Port | |
| 10.7 | Tasks | Open, overdue, completed on time / late, average completion time; by priority and user; oldest overdue | Port | |
| 10.8 | Collections / AR aging | Depends on collections | Drop | |
| 10.9 | Commission | Depends on commissions | Drop | |
| 10.10 | Report access | Tabs chosen by hard-coded role names; the `reports.*` permissions are never checked | Rework | Gate each report by permission. Add charts and CSV export for every report (OES3 has no charts). Fix the end-date-excluded bug. |

## 11. Resources

| # | Feature | What OES3 actually does | Status | OES4 notes |
|---|---|---|---|---|
| 11.1 | Company document library | Admin uploads (category, name, description, active); users browse by category; docs can be attached to emails | Port | Per-tenant categories (hard-coded in OES3) and private storage. |
| 11.2 | Training & Resources page | 4 static JRS training guides plus about 24 unlinked drafts (drip emails, agreements, decks) | Decide | Tenant-editable resource links or pages, or drop. The JRS content stays with JRS. |

## 12. Integrations (all Later)

| # | Integration | What OES3 actually does | Status |
|---|---|---|---|
| 12.1 | CompanyCam | Photos tab on records/projects, link or create CC projects, webhook, push generated docs | Later |
| 12.2 | FreePBX call reports | CDR sync script, call reports page (rep activity, volume and heat map, dispositions), user PBX extensions | Later |
| 12.3 | RoofHub | SKU fields and price snapshot columns only; pricing never built | Later |
| 12.4 | QuickBooks | Not built (only a "Job Cost (from QuickBooks)" label) | Later |
| 12.5 | HailRecon / IHM | IHM webhook (see 4.10); HailRecon link only | Later |

---

## 13. Features only in the live OES3 (re-audit 2026-11-16)

### 13.1 Properties and measurements
| Feature | What live OES3 does | Status | OES4 notes |
|---|---|---|---|
| Properties | Created automatically from a job's site address (normalized address + zip key). A property page lists jobs at the address and measurement history. No list, search, nav, create/edit, contacts or merge. Not linked to consumers/companies. | Rework | Becomes the **root record**, chosen explicitly, with a merge tool. See the design doc §3. |
| Property measurements | Versioned per property × trade, with source (manual/EagleView/Hover/insurance scope). Roof fields; gutter/siding values per elevation plus openings. A job keeps its own copy. | Rework | Add structures. Never let a partial job overwrite the whole building. Keep the source PDF. Freeze measurements into estimate versions. **Add a standalone Measurements page** (owner request; OES3 only has measurements inside the wizard). |
| Measurement report reading | Sends EagleView/Hover/insurance-scope PDFs to Claude for structured extraction (roof and elevations); the user reviews before saving | Port (Decide timing) | It's AI-based, so it needs per-tenant AI keys and budget, a data-processing review, and output encoding. |
| Address duplicate check | Warns about duplicate addresses on new consumer/company | Port | Use one address normalizer. Add permission checks (OES3 leaks other records' details). |

### 13.2 Estimating
| Feature | What live OES3 does | Status | OES4 notes |
|---|---|---|---|
| Two pricing modes per estimate | `srs` (takeoff + line items) or `costbook` (price book), set at create and switchable while empty | Port | Decided: keep both methods. |
| Price book (catalog) | 23 categories, about 6,500 Xactimate-derived items × activities. Coverage notes. Allowed products per item. "Core" items flag. Loaded by SQL only; no price-editing screen. | Rework | Split into catalog, **many price lists per tenant**, and tenant cost overrides. Real importers and an editing UI. ⚠ Verisk licensing (design doc §4). |
| Estimate builder | Xactimate-style structure → area tree, item search, Misc items, inline qty/price, products and colors, line notes, bulk move, drag reorder, cost/price analysis | Port | Pick a price list per estimate. |
| Price approvals | Approval needed for Misc, unpriced, below-default, custom-product and below-carrier lines. Inbox; reject needs a note; issuing is blocked while lines are pending. | Port | Forbid self-approval; notify approvers. |
| Carrier scope import | Reads a carrier Xactimate PDF with Claude, matches lines (exact, then activity, then prefix, then similarity), assigns areas, prices at the carrier's price | Ported 2026-11-18 (exact code + activity match; matched lines at our price, others as custom lines at the carrier price) | Supplement compare is not built. |
| Takeoff wizard (line-item path) | Measurements → products (SRS catalog) → options → preview and margin. Structured rules engine (driver × multiplier × waste, rounding, conditions, per-product coverage). Scope wording templates. Line overrides survive regeneration. Gutters/siding per elevation with systems A–D. | Port | Abstract the supplier catalog away from SRS. Transactional generate. Admin UIs for fields, options and slots (SQL-only today). Explicit pitch and waste data. |
| Estimate versions and signing | Issue freezes a version and PDF with a content hash. Revise with a reason. Mark Signed (upload signed copy) auto-converts to a project. | Port | Freeze terms, unit prices and price provenance too. E-signature later? |
| Customer change orders | Draft → issued → approved/rejected. Lines add, remove or modify. Contract value = signed version + approved COs. | Port | Use the same line model as estimates. |
| Sold scope vs order sheet | On price book jobs the sold lines set the price; production keeps a separate order sheet. "Suggest from scope" builds it from takeoff rules. | Port | Remove the hard-coded unit conversions. |
| Inactive/snoozed estimates | Shelve with a date; Active/Inactive/All filter; bulk bar | Port | |

### 13.3 Production
| Feature | What live OES3 does | Status | OES4 notes |
|---|---|---|---|
| Closeout Documents stage | New trade stage between Work In Progress and Final Walkthrough; creates a task for finance | Port | Use stage roles. |
| Per-move automation checkboxes | Before a stage move, a list of automations to tick or untick; skipped items are logged | Port | The server must re-plan and enforce the choices. |
| Remove from Board | Reasons, note, locks edits, restore to prior state; removed jobs leave the reports | Port | |
| Multi-role users | 6 roles (sales_rep, production_rep, finance_mgr added); permissions are the union of a user's roles | Port | Roles are tenant data. |
| Role-based assignment | Tasks assigned by role with sticky round-robin per project | Port | |
| **Subcontractor work orders** | Drafted from a rate matrix or the sold scope. Issued with a frozen amount. Change requests, reductions, pay requests, sub invoices, date proposals, closeout and payments; WO and closeout PDFs. Work Orders page. | Port (decided 2026-11-16) | Safe per-tenant numbering; transactional payments. The WO PDFs come with the "Later" documents unless needed sooner. |
| **Sub portal** | Public link (token in the URL) where the sub accepts/declines, proposes dates, submits change and pay requests with photos | Port (decided 2026-11-16) | Hashed, expiring, rotatable tokens; `no-referrer`; link sent automatically; optional sub logins later. |
| Subcontractor rates | Effective-dated rates by takeoff driver or price book item, labor-only or installed | Port | |
| Supplement request | Cover-sheet modal plus a history table. The full supplement-tracking spec is **not built**. | Port (simple) | The full spec is new work. |
| RoofHub / SRS ordering | Branch sync, live catalog/pricing, order submit, webhooks (confirmed, shipped, delivered, invoiced), thumbnails | Later | Integration. |

### 13.4 Communication and marketing
| Feature | What live OES3 does | Status | OES4 notes |
|---|---|---|---|
| **Email blasts** | Audience builder (presets + filters), template, throttle, schedule, recipient snapshot, progress, CSV export | Port (decided 2026-11-16) | If ported: verified senders, postal address in footer, one-click unsubscribe, atomic sending. |
| Suppressions and unsubscribe | Global suppression list (unsubscribe, manual, bounce, complaint) checked on every send; HMAC unsubscribe links; NDR bounce detection | Port (with blasts) | Separate marketing from transactional suppression. |
| **Online booking** (replaces Calendly) | Public page with booking types, hosts, weekly availability, buffers, notice, caps, assignment modes; matches or creates a lead; Outlook push; .ics; manage link. **Built, not deployed.** | Port (decided 2026-11-16) | Strong SaaS feature. Needs captcha/rate limits and per-tenant time zones. |
| Outside busy calendars | ICS links per user (with recurrence), blocking booking and shown on the calendar | Port (with booking) | Safe fetching (SSRF). |
| Email signatures and photos | Per-user signature (name, title, booking link) and profile photo | Port | |
| Email improvements | UTC fix, sent-mail de-duplication, Reply-To, To/CC pickers, more merge fields | Port | |

### 13.5 AI assistant ("Ridge")
| Feature | What live OES3 does | Status | OES4 notes |
|---|---|---|---|
| Chat assistant | Claude-based chat on every page with record tools (record, scope, notes, documents, price book, measurements, knowledge base, web search). Drafts letters and fills PDF forms; humans save. Usage and cost tracking, daily per-user limit, knowledge base admin. | Port (decided 2026-11-16) | If kept: record-level authorization on every tool (missing in OES3), tenant keys and budgets, data-processing terms, tenant-editable instructions instead of a JRS prompt. |

### 13.6 Inbound lead sources (all Later)
Fixr (lead marketplace) and RoofingEstimates.ai webhooks, IHM commercial path, email lead monitor. Build **one generic per-tenant inbound-lead webhook** later.

### 13.7 Generated documents (live)
Proposal (area layout from frozen versions, optional line prices: **Port**). Later: Materials List (no prices now), Certificate of Completion, Statement of Work (carrier letter), Final Invoice Letter (no amounts now), Class 4 letter, Impact Rating Certificate, Supplement Request cover sheet, AOB, Change Order, Work Order, WO Closeout, AI letters, AcroForm fills.

---

## Cross-cutting multi-tenancy work

- `tenantId` on every tenant-owned table, enforced centrally (EF Core global filters), never query by query.
- Every lookup table is per tenant and seeded from defaults at tenant creation: lifecycle stages, workflow stages (with roles), dispositions, prospect statuses, trade categories, project types, document categories, lead sources, appointment types.
- Per-tenant numbering sequences (estimate numbers).
- Per-tenant private file storage with authorized downloads.
- Scheduled jobs (email/calendar sync, nurture, notifications, lead monitor) run per tenant from one in-app job runner instead of public URL endpoints with secret keys.
- Webhooks resolve the tenant safely (Twilio by phone number; others by a per-tenant URL token).
- Per-tenant time zone, company info, branding and document terms.
- JRS becomes tenant #1, with a data migration from the OES3 MySQL database.
