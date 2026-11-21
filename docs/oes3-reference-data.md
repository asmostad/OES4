# OES3 Reference Data

> **Live-code additions (2026-11-16).**
>
> **Roles** are now 6: admin, sales_mgr, production_mgr, finance_mgr, sales_rep (was rep), production_rep. A user can hold several.
>
> **New permission keys (18):**
> - AI: ai.assistant, ai.usage, ai.knowledge
> - Booking / blasts: booking.manage, email.blasts
> - Change and work orders: changeorders.manage, workorders.manage, workorders.approve
> - Price book: estimates.costbook, pricebook.approve, pricebook.manage
> - RoofHub: roofhub.browse, roofhub.pricing, roofhub.order, roofhub.sync
> - Takeoff: takeoff.run, takeoff.manage, takeoff.bind
>
> **New setting groups:** ai, booking, blasts, fixr, reai, roofhub, takeoff (default margin 35%, sales tax 7%), assignment (round-robin cursors). Plus company_license, company_website, company_legal_name, letter_signer_title, system_user_id.
>
> **Trade stages are now:** Trade Setup, Material Orders, Material Deliveries, Work In Progress, **Closeout Documents**, Final Walkthrough, Completed.
>
> **Price book categories:** 23 Xactimate-style codes (RFG, SDG, SFG, WIN, …), mapped to trades.

Taken from `C:\Dev\OES3` schema, migrations and services (2026-11-15). Use it as the starting point for OES4's per-tenant default seed data.

## Roles and permission keys

Roles: **admin (A)**, **sales_mgr (S)**, **production_mgr (P)**, **rep (R)**. Admin bypasses every check in OES3.

| Area | Key → roles |
|---|---|
| Records | records.view_all (all)\*, records.create (all)\*, records.edit_own (all), records.edit_all (A,S,P), records.delete (A)\*, records.reassign (A,S)\*, lifecycle.convert (A,S,P,R) |
| Projects | projects.create (all)\*, projects.edit_own (all), projects.edit_all (A,S,P), workflow.advance (A,S,P), financials.edit (A,S) |
| Estimates | estimates.create (A,S,P,R), estimates.manage (A,S,P), estimates.view_all (A,S,P)\*, estimates.view_own (all)\* |
| Leads | leads.import (A,S), leads.prospect (A,S,P,R), leads.campaigns (A,S), leads.monitor (A,S) |
| Email / SMS | email.send (all), email.view_all (A,S,P), email.view_own (all), email.templates (A)\*, sms.send (all), sms.view_all (A,S,P), sms.view_own (all) |
| Tasks | tasks.create (all), tasks.manage (A,S,P), tasks.view_all (A,S,P), tasks.view_own (all) |
| Calendar | calendar.view_own (all), calendar.view_all (A,S,P), calendar.manage (A,S,P) |
| Nurture / automation | nurture.manage (A,S), nurture.enroll (all), automation.manage (A) |
| Admin | users.manage (A), settings.manage (A), products.manage (A,S,P), insurance_companies.manage (A,S), subcontractors.manage (A,P), documents.templates (A) |
| Reports | reports.view (A,S,P)\*, reports.view_limited (R)\* |
| Calls (later) | calls.view_all (A,S,P), calls.view_own (all) |
| Dropped | collections.manage, collections.view_all, collections.view_own, commissions.manage, commissions.view_own, weather.manage |

\* Seeded but never checked anywhere in the OES3 code.

## System settings (by group)

- **company:** company_name, company_phone, company_email, company_address. Also used but never seeded: company_city, company_state, company_zip.
- **app:** app_environment, app_site_url, estimate_number_prefix (EST), automation_from_email, notification_from_email, notification_check_key, proposal_print_key, default_tax_rate (unused), records_per_page (unused).
- **leads:** lead_inuse_timeout_minutes (5), lead_default_recycle_days (90, unused), lead_monitor_enabled / _secret / _shared_mailbox / _delta_link.
- **nurture:** nurture_enabled, nurture_batch_size (25), nurture_from_email, nurture_execute_key.
- **twilio:** twilio_account_sid, twilio_auth_token, twilio_phone_number, twilio_sms_enabled.
- **ms365:** ms365_tenant_id, ms365_client_id, ms365_client_secret, ms365_sync_enabled, ms365_calendar_sync_enabled, ms365_calendar_sync_secret, ms365_sync_secret (never seeded). Unused: ms365_shared_mailbox, ms365_sync_interval, ms365_sync_lookback_days.
- **Integrations (later):** pbx_*, companycam_*, roofhub_*, ihm_*.

## Lookup and seed data

- **Lifecycle stages:** Lead (#3b82f6), Prospect (#f59e0b), Customer (#10b981).
- **Project types:** Insurance Claim, Retail Project, Service & Maintenance, Warranty. (Monitoring Inspection was added for weather monitoring, which is dropped.)
- **Project stages:** Verification, In Production, Waiting for Invoice, Waiting for Payment, Paid, Closed.
- **Trade stages:** Trade Setup, Material Orders, Material Deliveries, Work In Progress, Final Walkthrough, Completed.
- **Trade categories:** Roofing, Gutters, Windows, Siding, Paint, Fencing, General.
- **Lead dispositions** (recycle days):
  - not_interested 180, callback none, needs_info 30, no_answer 7, left_voicemail 14, wrong_number none, do_not_call none.
  - Converting dispositions (each sets a starting prospect status): qualified → initial_contact, appointment_set → inspection_scheduled, agreement_sent → proposal_sent.
- **Prospect statuses** (stale threshold in hours): initial_contact 72, inspection_scheduled 48, inspection_completed 72, claim_filed 336, adjuster_scheduled 48, proposal_sent 72, decision_pending 168, follow_up_needed 72.
- **Attachment categories:** Insurance Scopes & Supplements, Contracts & Change Orders, Invoices & Receipts, Estimates & Proposals, Photos, Permits & Inspections, Warranties, Communications, Other / Misc.
- **Company doc categories** (hard-coded): Insurance Certificates, Licenses & Permits, Tax Documents (W-9), Safety Documents, Company Policies, Other.
- **Insurance carriers (15):** State Farm, Allstate, USAA, Liberty Mutual, Farmers, Nationwide, Progressive, American Family, Travelers, Erie, Auto-Owners, The Hartford, Chubb, Cincinnati, Safeco.
- **Lead source suggestions:**
  - Consumer: Website, Referral, Canvass, Door Knock, Phone Call, Social Media, Google Ads, Home Show, Storm Chase, Insurance Agent, Repeat Customer.
  - Company: Website, Referral, Cold Call, Trade Show, Google Ads, Social Media, Insurance Agent, Property Manager.
- **Product units:** each, sq ft, linear ft, square, bundle, gallon, roll, sheet, box, pair, set, hour, day, job.

## Enums

- **Task priority:** low, normal, high, urgent. **Task status:** pending, completed, cancelled.
- **Estimate status:** draft, sent, accepted, declined, expired, revised.
- **Appointment type:** inspection, adjuster_meeting, customer_meeting, crew_scheduling, supplement_meeting, final_walkthrough, estimate, sales_call, office_meeting, other. **Appointment status:** scheduled, completed, cancelled.
- **Supplement status:** none, needed, sent, approved, denied.
- **Line type:** material, labor, sales_tax, equipment, fee, discount.
- **Trade status:** draft, submitted, approved, in_progress, completed, cancelled.
- **Nurture step:** send_email, send_sms, create_task, wait. **Enrollment status:** active, paused, completed, cancelled.
- **Subcontractor document type:** insurance, license, agreement, w9, other.

## Merge fields (email, SMS and documents)

- **consumer:** `consumer.firstName`, `.lastName`, `.email`, `.phone`, `.phoneMobile` (SMS only).
- **company:** `company.companyName`, `.email`, `.phone`.
- **customer:** `customer.name`, `.firstName`, `.lastName`, `.email`, `.phone`. Resolves to the consumer, or to the company plus its primary contact.
- **contact:** `contact.name`, `.firstName`, `.lastName`, `.email`, `.phone`, `.title`.
- **project:** `project.projectName`, `.siteAddress1`, `.siteCity`, `.siteState`, `.siteZip`, `.claimNumber`, `.policyNumber`, `.dateOfLoss`, `.deductible`, `.adjusterName`, `.adjusterPhone`, `.adjusterEmail`, `.insuranceCompany`.
- **insurance:** `insurance.companyName`, `.phone`, `.fax`, `.email`, `.claimsEmail`, `.claimsFax`, `.preferredMethod`.
- **trade:** `trade.tradeName`, `.tradeDescription`, `.materialDate`, `.workDate`.
- **crew:** `crew.companyName`, `.leaderName`, `.phone`, `.email`, `.specialty`, `.photo` (email only), `.photoURL` (email only).
- **user:** `user.firstName`, `.lastName`, `.email`, `.phone`. In email this resolves to the project owner; in SMS, to the sending user.
- **global:** `company_name`, `company_phone`, `company_email`, `company_address`, `current_date`.

## Workflow automation triggers

| Trigger | Fires on | Actions in OES3 |
|---|---|---|
| onProjectCreated | Project created or estimate converted | Email `auto_project_welcome` |
| onMaterialOrders | Trade enters Material Orders | Email `auto_materials_ordered`; in-app notice to all production managers |
| onMaterialDeliveries | Trade enters Material Deliveries | Materials List PDF; email `auto_materials_arriving` with the PDF; task |
| onWorkInProgress | Trade enters Work In Progress | Email `auto_work_starting` |
| onWorkCompleted | Trade enters Work Completed (stage now inactive) | Email `auto_work_completed` |
| onFinalWalkthrough | Trade enters Final Walkthrough | Certificate of Completion; task "Get Certificate of Completion signed" |
| onAllTradesComplete | All trades done → project auto-moves to Waiting for Invoice | SOW plus Final Invoice Letter (plus Impact docs if insurance); task "Create invoice…" |
| onLaborScheduled | Trade work date set for the first time | Email `auto_labor_scheduled` plus SMS `auto_labor_scheduled_sms` |

**Internal notification toggles:** notify_task_assigned, notify_task_reassigned (has no effect), notify_task_overdue, notify_task_due_soon, notify_appointment_assigned, notify_prospect_stale (never wired up).
