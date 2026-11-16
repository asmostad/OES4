# OES3 Known Issues: Don't Carry These Forward

These came from the full code read of `C:\Dev\OES3` on 2026-11-15. They are listed so OES4 requirements and tests cover them. File references point into the OES3 codebase.

## A. Act now in OES3 (production secrets)

1. **The production admin password is in plain text** in `CLAUDE.md` and `database/migration_clear_test_data.sql:246,264`, alongside its hash and the owner's name and email.
2. **The FreePBX database password is in plain text** in `cdr_sync.sh:12`.
3. **`docs/MS365 App - Secrete Value and ID.txt`**: judging by the filename, it likely holds the Azure app client secret. (Not opened.)
4. `admin/settings.cfm` writes every stored secret (Twilio token, M365 secret, API keys) into the page HTML.

Rotate the admin password, the PBX password and the M365 client secret, then remove them from files that sync to Google Drive or get committed.

**The live copy (`J:\My Drive\Sites\JohnsonRoofers.com\OES3`) has the same problems,** plus the following:
- `CLAUDE.md:2626-2632` still has the VPS admin username, password and hash in plain text.
- `ROOFHUB_DEMO_TEST.md` (around line 13) has a webhook-secret sample. Check that it isn't the real secret.
- `admin/settings.cfm` now also renders the Anthropic API key and the Fixr, RE.ai and RoofHub secrets into the page.
- The `forms/` folder and the "Estimating Redesign" folder sit in the web root. They hold fillable PDFs, the Verisk price-list PDF and sample insurance scopes containing claimant personal data.

## A2. Highest-risk issues found only in the live code (fix in OES3 now)

| Issue | Where |
|---|---|
| The AI assistant opens a chat on **any** record by ID, and every role has `ai.assistant`, so any user can read any customer's data through its tools | `api/assistant.cfm:137` |
| Email blast "From" is free text. App-level Graph permission means a blast can send **as any mailbox in the M365 tenant**. | `api/email_blasts.cfm:139` |
| The blast footer has no postal address, which CAN-SPAM requires | `EmailBlastService.cfc:1295` |
| SMS has no STOP/HELP handling, so it isn't A2P/TCPA compliant | `api/sms_webhook.cfm` |
| After signing, a sold price can be changed without a change order: edit or delete the price book "twin" line by ID | `trade_view.cfm:142`, `ProjectService.cfc:1111,1158` |
| The proposal PDF fetches a user-supplied image URL server-side (SSRF) on a public print page | `proposal_print.cfm:879` |
| All RoofHub API POSTs skip CSRF when the token field is omitted, including order submit | `api/roofhub.cfm:55` |
| Work order PDFs print the sub's portal link and sit in public `/uploads/generated/` with guessable names | `work_order_print.cfm:29` |
| `property_view`, attachment list/zip/upload, and `dupe_check` have no record-level check | various |
| The email signature form has no CSRF check, so a forged request can plant a link in a user's signature | `includes/email_signature_card.cfm:18` |
| Saving your profile wipes `ms365Email`, `pbxExtension`, `hireDate` and the Fixr/RE.ai round-robin flags | `profile.cfm:16-24` |
| Busy-calendar ICS fetch follows redirects with no private-IP check (SSRF) | `BusyCalendarService.cfc:175` |
| Booking builds email links from the Host header (host-header injection). Anonymous bookers can write onto, and claim, existing records. | `book.cfm:18` |
| LLM output from uploaded PDFs is written into `innerHTML` (DOM XSS) | `takeoff_wizard.cfm:1773` |
| Sidebar AI admin links check the nonexistent permission keys `navItm.knowledge` / `navItm.usage` | `includes/header.cfm:200` |

## B. Security design flaws (OES4 must do the opposite)

| Issue | Where in OES3 |
|---|---|
| CSRF isn't enforced. A bad token only sets a message and the form still processes. `/api/` skips CSRF entirely unless each endpoint checks, and many don't (email send, documents, attachments, CompanyCam). | `Application.cfc:182-190` |
| Destructive actions run as GET links with no token: set stage, delete note/attachment/contact/line item/trade, deactivate user, toggle/delete templates, delete company docs | many pages |
| Passwords are unsalted SHA-384. No lockout or rate limit, no session rotation at login, no HttpOnly/Secure cookie settings. The deactivated-account check reveals which usernames exist. | `UserService.cfc:38,90-110` |
| **Uploads:** `strict=false`, only the browser-supplied MIME type is checked, and the extension isn't allow-listed. Files go into the web-served `/uploads/`, so code execution is possible. | `AttachmentService.cfc:13-42` |
| **Downloads aren't authorized.** Attachments, generated PDFs (guessable names), company docs and subcontractor docs are all public static URLs. | `/uploads/` |
| No record-level view check. Any user can open any consumer, company, project, estimate or trade by ID, including print pages and attachment lists. Search returns everything. | view pages, `api/search.cfm`, `api/attachments.cfm` |
| IDOR: trade, line item, contact, note, attachment and collection IDs are never checked against the parent record on the page | project/trade/estimate/company views |
| Stored and reflected XSS: names, descriptions, flash messages and activity text are output unencoded. The search dropdown and toasts insert raw HTML. `url.board` is reflected into JS. | lists, `oes3.js:192`, `workflow.cfm:552` |
| The Twilio webhook signature check only runs if the header is present. Omit the header and the check is skipped (spoofable). | `api/sms_webhook.cfm:22-43` |
| Scheduled and print endpoints authenticate with a secret in the URL query string (ends up in logs), compared as plain strings | `api/*_sync.cfm`, `*_print.cfm` |
| Permissions are cached for the whole session. Role changes and deactivation only take effect at next login. | `Application.cfc` |
| Several screens check role **names** instead of permissions. 10 permission keys are never checked at all. | see reference doc |
| PDF generation stores per-request data on the application singleton, so concurrent generations can mix customer data | `DocumentGenerationService.cfc:617-699` |
| Email attachment IDs aren't checked against the record or user (IDOR) | `api/email.cfm:56-99` |

## C. Functional bugs worth writing tests for

**CRM and leads**
- Sort links drop the source/SIC filter.
- Lead locks are check-then-update (not atomic). The server doesn't enforce another user's lock when setting a disposition.
- Dispositions don't check ownership or that the record is still a Lead.
- Auto-convert writes two activity rows.
- CSV import: duplicates within the file aren't detected; quoted newlines break parsing; client-side validation disagrees with the server.
- The email lead monitor test-parse panel turns "Strip HTML: No" into Yes.
- Duplicate notes and fallback owners use a hard-coded userID 1.
- Round-robin isn't concurrency-safe.

**Projects and production**
- Line-item color/size are never saved (form field name mismatch, `trade_view.cfm:113-114` vs `1090,1096`).
- Apply Margin zeroes zero-cost lines and marks up tax and discount lines.
- An estimate trade can be pushed into production by URL.
- Editing a project or estimate as a non-manager with edit_all silently reassigns the owner to you.
- Estimate numbers race (MAX+1).
- Convert and duplicate aren't transactional.
- Statement of Work "Date of Completion" always shows today (the loader omits workDate).
- The Final Invoice Letter ignores payments and deductible.
- Trade tradeStatus is never set to completed.
- Auto-advance uses a hard-coded stageOrder 7.
- The product import CSV parser corrupts quoted JSON.
- The products page's show-inactive param is broken.

**Communication**
- Email initial sync never finishes on mailboxes over about 5,000 messages (no delta link is saved after the 100-page cap).
- Graph UTC times are stored as local time.
- Rollups show the same email N times.
- The email hub has no compose button.
- The SMS `user.*` merge field means something different from email's.
- Staff notification emails are logged into customer email history.

**Tasks, calendar and notifications**
- The task rollup status filter has an AND/OR precedence bug (`TaskService.cfc:434-445`).
- Priority sorts alphabetically.
- Calendar drag/drop stores UTC as local time.
- All-day events gain a day.
- An appointment cancelled after being modified is never deleted in M365.
- The conflict state is terminal.
- Attendees create duplicate appointments on pull.
- The FullCalendar CSS URL is a 404.
- Overdue notifications repeat forever once read.
- The `notify_task_reassigned` toggle does nothing.
- The `inAppEnabled` preference is ignored.

**Nurture**
- The first step's delay is ignored.
- Failed steps advance anyway.
- Resume skips the remaining delay.
- No opt-out or do-not-contact check.

**Platform**
- Saving your profile wipes hire date, M365 mailbox and PBX extension.
- An admin editing their own user record deactivates themselves.
- Report date ranges drop the last day. Closed projects count as pipeline and as stale.
- A `calls.view_own` user with no extension sees all calls.
- Some settings are used by the code but never seeded (`company_city/state/zip`, `ms365_sync_secret`).

## D. JRS / single-tenant hard-coding to make configurable

- **Name and URLs:** "Johnson Restoration Services" fallbacks (merge fields, PDFs, footer); `https://johnsonroofers.com/OES3` and the `/OES3` base path (notifications, Twilio callback, PDFs, JS fallbacks).
- **Branding:** JRS logos; `--jrs-*` PDF colors; "OES3:" email subject prefix.
- **Proposal terms:** "JRS does not require deposits…", and a Nebraska statute late fee (`proposal_print.cfm:741-749`).
- **Workflow:** every workflow stage name; stage ID 1; stageOrder 7; project type ID 1 = insurance; the literal type name "Insurance Claim".
- **Thresholds and lists:** pipeline stage weights; 30% margin rule; 3/7-day stage badge thresholds; lead source lists; company doc categories.
- **Locale:** "Central Standard Time" for calendar sync; US-only phone formatting.
- **Paths and placeholders:** Windows Chrome paths; Oklahoma/Nebraska placeholder phone numbers; training files with JRS phone numbers and links.
