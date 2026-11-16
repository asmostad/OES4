# OES4 Design Decisions

Living record of what has been decided for OES4, what is proposed, and what is still open. Feature-level port decisions are in [oes3-feature-inventory.md](oes3-feature-inventory.md).

Status: **Decided** = the owner said so. **Proposed** = recommended, needs a yes. **Open** = needs a decision.

---

## 1. Platform

| Topic | Decision | Status |
|---|---|---|
| Hosting | Existing Windows Server / IIS VPS, for now | Decided 2026-11-15 |
| Stack | ASP.NET Core (C#), Razor Pages, EF Core with global tenant query filters | Decided 2026-11-16 |
| Target market | **Insurance / storm restoration contractors first**: claims, carriers, supplements, adjusters and storm lead flow are front-line features. Keep the design usable for retail contractors (insurance sections hide on retail jobs). | Decided 2026-11-16 |
| Database | MySQL 9.6 | Proposed |
| Look and feel | Keep the OES3 design system (Bootstrap 5.3.3, Bootstrap Icons, Inter, `oes3.css` tokens/components) | Decided (goal) |
| Mobile | iOS + Android after the web launch, covering most features. The backend is API-first (`/api/v1`) from day one. | Decided (requirement) |
| Auth | ASP.NET Core Identity; cookies (web) + OAuth2/OIDC tokens (mobile) | Proposed |
| Tenant isolation | One shared database, `TenantId` on every tenant-owned row, enforced by global filters | Proposed |
| Tenant URL | **One domain.** Tenant resolved from the user's account at login (tenant picker if a user belongs to several). | Decided 2026-11-16 |
| File storage | **VPS disk for now**, private per-tenant folders outside the web root, downloads through an authorized endpoint. All file access goes through a storage abstraction (`IFileStore`) so we can move to S3 (or another cloud store) later without code changes. | Decided 2026-11-16 |
| Email to CRM contacts | Sent **from the tenant's own mailbox through their Microsoft 365** (Graph, per-tenant OAuth consent). Other providers (e.g. Gmail) later, behind an email-provider abstraction. | Decided 2026-11-16 |
| System notices | Password resets, invites, staff notifications etc. are sent **from our own domain via Amazon SES** | Decided 2026-11-16 |
| Background jobs | In-app job runner (e.g. Hangfire), per tenant, replacing the public "?key=" scheduled URLs | Proposed |

## 2. Scope (from the owner)

- **Drop:** collections, commission engine, chargebacks, weather monitoring, document template builder.
- **Keep:** project-level financials.
- **Port now:** the proposal print. **Later:** every other system-generated document (SOW, materials list, certificate, final invoice letter, Class 4 / impact, AOB, supplement request cover sheet, work order PDFs, form fills).
- **Later (integrations):** CompanyCam, FreePBX call reports, RoofHub/SRS, QuickBooks, HailRecon/IHM, the email lead monitor, Fixr, RE.ai. Design so these plug in later.
- **Keep (decided 2026-11-16):** subcontractor work orders and the sub portal, email blasts, online booking, the AI assistant.

## 3. Core data model: the property is the root (Decided in principle, 2026-11-15)

```
Tenant
 └─ Property (root)                        address, geocode, normalized key (unique per tenant)
     ├─ Structure                          Main House, Detached Garage, Building B…   (Proposed)
     │    └─ Measurement Set (versioned)   per trade; source report linked             (Proposed)
     ├─ Property ↔ Contact (role)          owner, tenant, property mgr, agent… with start/end dates
     ├─ Estimate ─┐
     └─ Project  ─┴─ Trade ─ Line items / scope
                    └─ Project ↔ Contact (role)   homeowner/insured, decision-maker, billing…

Contact (one table)
 ├─ type = Person | Organization           (the owner's "isCommercial" idea)
 └─ Person ↔ Organization (title, isPrimary)
```

**Decided (owner, 2026-11-15):**
- The property is the root record. Contacts, estimates and projects hang off it.
- Contacts and properties are many-to-many, and so are contacts and estimates/projects.
- A single contacts table replaces consumers + companies + company_contacts, with a commercial/organization flag. Person contacts can belong to an organization contact.
- An organization page lists its people (and lets the user add one). A property page lists all its contacts. A contact page lists all its properties.
- Measurements belong to a property.

**Proposed refinements (need a yes):**
1. **Contact type instead of a boolean.** Use `ContactType = Person | Organization`, plus a Person→Organization link table (title, isPrimary, active). This gives you the "isCommercial" behavior and allows one person to work for two organizations.
2. **Roles on every link.** `PropertyContact.Role` and `ProjectContact.Role` come from per-tenant lookups. Links have start/end dates and are never deleted, so you keep history when a house sells.
3. **Structures under a property.** OES3 has no way to measure a house and a detached garage separately. Price-book estimate "areas" already have structures, so they can share this table.
4. **Measurements are versioned per structure + trade, and never overwritten.** A new report creates a new version. The source PDF is always saved as a document and linked. A job can use a version, and the signed estimate freezes the values it used. OES3 already versions per property and trade; OES4 adds structures and keeps source files. It also avoids OES3's bug where a partial job overwrites the whole building's measurements.
5. **Properties are chosen explicitly** (typeahead plus duplicate check) when a contact, estimate or project is created. They are not silently derived from a job address the way OES3 does it. Include a merge tool for duplicate properties in v1.
6. **Notes and documents link to multiple levels.** A note on a trade is also visible on its project, property and contacts. Each view has a level filter ("This record / Everything under it"), replacing OES3's All/This toggle.

### 3.1 Opportunities: the sales pipeline (Decided 2026-11-16)

- The sales pipeline lives on an **Opportunity** record (standard CRM language), not on the contact.
- **A lead is an opportunity.** It needs a contact but **not a property**. Most leads arrive without an address. When the address is collected, the system **attaches the opportunity to an existing property, or creates a new one** (with the duplicate/near-match check).
- The opportunity carries: source, owner, pipeline status (OES3's dispositions and prospect statuses become per-tenant pipeline stages), contact attempts, recycle date, lock, and lead-intake details (e.g. storm/date of loss).
- An opportunity turns into **estimates and projects** at its property. One contact can have several opportunities (different properties, or a new storm years later).

**Proposed details (need a yes):**
- The contact keeps only a simple relationship status (e.g. Lead / Customer / Past Customer), derived from its opportunities and projects, for lists and blasts.
- Opportunity outcomes: Open → Won (became a project) / Lost (with reason) / Recycled.
- Prospecting tools (back/next through a filtered list, locking, dispositions) work on opportunities.
- Inbound lead sources (booking page, imports, webhooks later) all create opportunities.

## 4. Estimating (Decided 2026-11-15)

- **Two ways to estimate**, chosen per estimate:
  1. **Price book estimate** (Xactimate-style): structure → area → line items picked from a price list.
  2. **Line-item estimate**: free-form lines from the product catalog / takeoff rules. OES3's takeoff wizard and order sheet live here.
- **Price lists are separate from line items.**
  - A tenant has **many price lists**: name, region code, effective date, source (own / imported), active.
  - Price list rows are per item + activity (Remove, Replace, R&R…).
  - When creating a price book estimate, the user **picks the price list**, the same way you pick one in Xactimate.
  - Each estimate line **copies** the price row ID plus unit price, cost and default price. Updating or replacing a price list never changes existing estimates. OES3 already snapshots; OES4 adds multiple dated lists.
- **Importing price lists** from Xactimate is a goal.
- Insurance **carrier scope import** (read the carrier's Xactimate PDF and match lines) exists in OES3 using Claude. It's a port candidate.

**Proposed:**
- Separate the **catalog** (item codes, descriptions, units, categories, coverage notes) from **price lists** (prices per item + activity, per list) and **tenant cost overrides** (material/labor/equipment cost, minimum price).
- Estimate versions freeze terms, unit prices and price provenance (OES3 freezes only line totals). Contract value = signed version + approved change orders. Change orders use the same line model as estimates.
- Add explicit **O&P, tax and waste** settings per estimate (OES3's price book path has none).
- Drop OES3's hidden "twin line" workaround. Totals are computed from estimate lines.
- Keep exception-based **price approvals** (Misc items, below default, unpriced, custom product), but forbid self-approval and notify approvers.

**Licensing (deferred, 2026-11-16):** for now, users **import or create their own price lists**. OES4 ships no Xactimate data. Background, for when we revisit: OES3's price book was built by transcribing about 6,500 Xactimate items and prices from a Verisk price-list PDF (`NEOM8X_OCT26`). These are used as default sell prices, shown to reps and sent to the AI assistant. For a SaaS product:
- Never ship Verisk price data to tenants.
- Each tenant imports a list they are licensed to use.
- Review Verisk's terms before building "import from Xactimate".
- ESX files are encrypted, so imports would come from Xactimate's own export formats or PDFs.

## 5. Insurance carriers (Decided 2026-11-15)

- A **platform default carrier list** is shared by all tenants.
- Tenants can **edit their own copy** of any carrier (submission method, claims email/fax, special contacts, notes) and add their own. A tenant's edit never changes the default list.
- Proposed design: `Carrier` (platform), plus `TenantCarrier` (TenantId, optional CarrierId, override fields). A tenant sees the default list merged with its own overrides.

**Decided 2026-11-16:**
- **Storm events are first-class records** (date, peril, hail size/wind, area). Opportunities and claims link to a storm; lists, reports and blast audiences filter by storm.
- **Claims are optional up front.** An insurance opportunity can leave claim fields empty until the homeowner files.
- **Subcontractors are organization contacts** with a subcontractor profile, so they appear in contact search (filterable).
- **Display numbers** use the `P-2026-0311` style (prefix, year, per-tenant yearly sequence) for projects, estimates and work orders.

## 6. Record levels and navigation (Decided 2026-11-15)

- Each level (**Property, Contact/Organization, Estimate, Project, Trade**) must be instantly recognizable, with its own "dashboard" layout, not one shared template.
- **Breadcrumb** that shows the nesting and lets you go up a level in one click, e.g. `Property: 123 Main St › Project: Smith Roof 2026 › Trade: Roofing`.

**Proposed:**
- Each level gets an accent color, an icon and a distinct header band (e.g. property = slate/house, contact = blue/person, organization = indigo/building, estimate = amber/calculator, project = green/hammer, trade = teal/layers), using the OES3 tokens.
- The header always shows the parent chain as chips (whose house it is, which project).
- Each level's overview shows what matters at that level:
  - property: map, structures, measurements, contacts, jobs history
  - contact: properties, open jobs, comms
  - project: stage tracker, trades, financials
  - trade: schedule, line items, sub/work order
- Properties get a list page, search results, and a sidebar entry. (OES3's property page is reachable only from a job.)

## 7. Workflow

**Proposed:**
- Stages are per tenant, with **roles** (initial, production start, materials ordered, materials delivered, labor, closeout docs, final walkthrough, done). Automation and gates key off roles, never stage names or IDs. OES3 has a dead "Work Completed" trigger and hard-coded stage thresholds of 7 and 8 because of name/ID coding.
- Port OES3's per-move automation preview with checkboxes, but have the server re-plan the move and enforce the user's choices.
- Prevent re-firing when a stage is re-entered.
- Port **Remove from Board** (reasons, restore, locks).

## 8. Security baseline (Proposed: non-negotiable)

- Anti-forgery on every state change; **no GET mutations**.
- Record-level authorization on every page, API endpoint, AI tool and file download.
- Encode all output, including LLM output.
- Secrets encrypted, write-only in the UI.
- Upload allow-lists and size limits, stored outside the web root.
- External links (sub portal, booking manage, unsubscribe) use hashed, expiring, rotatable tokens and a `no-referrer` policy.
- Webhooks use per-tenant URLs plus HMAC with a timestamp, constant-time compare and idempotency.
- SMS: STOP/HELP handling and consent tracking before any SMS feature ships.
- Email marketing: CAN-SPAM (postal address, one-click unsubscribe, verified sender identities).
