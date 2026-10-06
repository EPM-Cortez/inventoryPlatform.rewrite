# Inventory Frontend (rewrite)

A React frontend for the single-school inventory API in [`../backend`](../backend/README.md).

This is a clean rewrite. It targets **only** the endpoints that backend actually
implements — assets, custody, people, departments, warranties, QR identifiers,
reference data and users. There are no trusts, schools, licences, loans, purchase
orders, SSO or alerts, because that backend has none of them.

## Prerequisites

- Node 22 or newer
- The API running from `../backend` (see its README for the database
  migration and first-administrator bootstrap)

## Run it

```bash
npm install
```

Start the API first, then:

```bash
npm run dev
```

The app is served at <http://localhost:5173> and proxies `/api`, `/health` and
`/openapi` to `https://localhost:7270`. Override the target with
`VITE_API_PROXY_TARGET` in a `.env.local` file — see `.env.example`.

| Command | What it does |
| --- | --- |
| `npm run dev` | Vite dev server with the API proxy |
| `npm run build` | Type-check, then build to `dist/` |
| `npm run typecheck` | Type-check only |
| `npm run lint` | ESLint |
| `npm test` | Vitest, once |
| `npm run test:watch` | Vitest, watching |

## The one architectural constraint

**The app and the API must be served from the same origin.** This is not a
preference — it is forced by the backend:

```csharp
options.Cookie.Name = "__Host-Inventory.Auth";
options.Cookie.SameSite = SameSiteMode.Strict;
```

`SameSite=Strict` cookies are never sent on cross-site requests, so a CORS
deployment cannot authenticate at all. In development the Vite proxy supplies the
shared origin; in production the built `dist/` must be served from the same host
as `/api`.

If sign-in loops or every mutation fails with `400 antiforgery-validation-failed`,
check the proxy before anything else. Adding CORS headers will not fix it.

### CSRF

Every unsafe request needs the antiforgery cookie plus an `X-CSRF-TOKEN` header.
`src/api/client.ts` handles this: it fetches the token lazily, caches it in
memory, de-duplicates concurrent fetches, and replays a request **once** if the
server rejects the token (which happens when the identity changes in another
tab). The token is explicitly discarded after login, logout and password change,
because the pair is bound to the signed-in identity.

## Layout

```text
src/
  api/          Contract types, HTTP client, endpoints, React Query hooks
  auth/         Session provider, permission constants
  app/          Shell, routing, navigation, theme, command palette
  ui/           Design-system primitives (no domain knowledge)
  features/     One folder per area of the product
  lib/          Formatting, class merging, URL state
  styles/       Tailwind 4 theme tokens
```

### `src/api` is the contract boundary

`src/api/types.ts` is hand-mirrored from `Inventory.Contracts.V1`. When the
backend changes a shape, change it there first and let the type errors show you
every call site. Do not widen a type to silence an error.

`src/api/endpoints.ts` has one function per implemented endpoint. If a function
is missing, the backend does not implement it.

## Accounts vs people

The two are deliberately separate, and conflating them is the easiest mistake to
make in this domain:

- **Accounts** (`/accounts`) are sign-ins. They have roles, passwords and
  sessions. Administrators and technicians need one.
- **People** (`/people`) are custody records — who is holding a laptop. They have
  no login and adding one grants no access.

A caretaker who holds equipment but never signs in needs only a person. An IT
technician needs both.

Archiving an account is a history-preserving soft delete: sign-in is disabled and
sessions end, but the account stays as the recorded author of its past asset
history and audit entries, and it can be restored. Nothing hard-deletes.

## Optimistic concurrency

Mutable records carry a `version` guid. Updates and lifecycle commands must send
the last-seen version; a stale one returns `409` and the server never overwrites.

Accounts are versioned too, with one shape worth knowing: **archive sends the
version as a query parameter** (`DELETE /users/{id}?version=…`) because DELETE
carries no body in this API. Every other account write puts it in the body. There
is a test pinning this in `src/api/usersApi.test.ts`.

Disable and archive answer `204`, so there is no fresh version to cache — the
hooks refetch, and without that the next action on the same row would send a
stale version.

Two rules keep this workable, both enforced in `src/api/hooks.ts`:

1. Any mutation returning the updated record writes it straight into the detail
   cache, so the next edit sends the version the server just issued.
2. Any mutation that can change derived state (status, holder, location, warranty
   summary) also invalidates the **lists**, which carry their own copies.

When a `409` does happen, the UI says so and asks the user to reload and reapply —
it never retries with the same version.

## Device inventory from the Windows agent

Machines running the agent enrol with a signed, TPM-backed key and check in with a
full snapshot of themselves. That data is **evidence, not a record** — collected
automatically, never edited from the UI, and always qualified by when it was
collected.

Two ways in:

- **Assets → Device tab** — what the agent reports for that specific asset. Most
  assets are not managed machines, so "no agent linked" is an ordinary state and
  the empty state says so rather than looking like a fault.
- **Devices** (`/devices`) — the fleet. Eight counts across the top, each one a
  filter: enrolled, not linked, conflicts, offline, late, never reported,
  dismissed, revoked. A count that cannot be clicked through to the machines
  behind it is a dead end, so every tile navigates.

A snapshot covers hardware, operating system, security posture, network adapters,
storage volumes and installed software (capped at 750 programs — see below).

Things the UI is deliberate about:

- **Connectivity comes from the server.** `connectivityStatus` is judged against
  the interval each agent is expected to report on, so it is never recomputed
  here from `lastCheckInAtUtc` — a fixed client-side threshold would disagree
  with the tile counts sitting right above the row.
- **Revoked needs two parameters.** Filtering to revoked machines sends
  `trustStatus=revoked` **and** `includeRevoked=true`; the first alone returns
  nothing, silently. The tile sends both.
- **Staleness.** A three-month-old snapshot describing a since-rebuilt machine is
  worse than none if the reader misses the date, so age is shown wherever a
  snapshot is, and turns amber past a week and red past a month.
- **Tri-state security.** The agent may fail to read a setting. "Unknown" renders
  differently from "off" — reporting an unknown as a failure sends someone
  chasing a problem that may not exist.
- **Linking is a trust decision.** Tying an agent to an asset asserts that this
  hardware *is* that asset tag, so the dialog shows what the machine says about
  itself to help confirm the match.

### Triage: what to do with a machine that has no asset

Four ways out of the review queue, and the difference between them matters:

| Action | What it does | Reversible |
| --- | --- | --- |
| **Link to existing** | Ties it to an asset already on the register | Yes, by unlinking |
| **Create asset** | Creates an asset *and* links it, in one transaction | Yes, by unlinking |
| **Dismiss** | Records that someone looked and it needs no asset | Yes, restore to review |
| **Revoke** | Withdraws trust from the machine's key | **No** — it must re-enrol |

The first three all take a required reason and the agent's `version`; they are
the reversible ones and read as ordinary work. Revoking is the odd one out, so it
alone asks for the hostname to be typed.

**Create asset** seeds every field from the snapshot but commits nothing until
someone has read it — that is the whole distinction between this and the agent
writing to the register itself. Sending the agent's `version` with the create
means a machine that changed underneath rejects the whole request rather than
leaving an orphaned asset behind. The payload is built by
`features/agents/agentAssetSeed.ts`, which is where the blank-string-to-`null`
rule and the asset-tag union are enforced, and is unit tested.

A **conflict** is not a worse kind of "not linked": the server found more than
one candidate asset, or one already linked to another machine. It renders in
danger tone with the server's own `matchReason`, because only a person can
resolve it.

`agents.read` is held by all four roles; `agents.manage` (linking, triage,
revoking, the installer, enrollment profiles) is administrator and manager only;
`agents.recover` is separate again, below.

### Key recovery

A motherboard swap or a cleared TPM leaves a machine that is still the same asset
but can no longer prove it. Key recovery authorises it to replace its signing key
without enrolling afresh, and the device page lists every authorisation with who
approved it, why, and where it ended up.

It is a **credential, not a record edit**, and the UI is built around that:

- It sits behind its own `agents.recover` permission rather than `agents.manage`.
  Being able to link or revoke a machine does not imply being able to re-key one.
- The window defaults to a day, not the weeks an enrollment token gets — the
  machine is expected to be in front of someone right now.
- "Require the new key to be TPM-held" defaults to on whenever the current key is
  TPM-held. A recovery must never be a route to a weaker key than the machine
  already has, so weakening it is a deliberate act.
- `pending` shows amber, not green: an unused authorisation is a live credential,
  and the wanted end state is `consumed`.
- Only `pending` offers Revoke. `consumed`, `expired` and `revoked` are history,
  and offering an action the server would refuse is worse than offering none.

Revoking an authorisation costs nothing — the machine keeps its current key and
keeps reporting; only the unused token stops working. That is the opposite of
revoking the *agent*, which is irreversible, and the copy says so in both places.

The list never carries the token, its hash, or a key thumbprint. What is
auditable is that someone authorised a re-key and why, not the material behind it.

### The installer

`/devices` shows the published MSI with its version, size, SHA-256, signer and an
unattended install command. The checksum is the reason the card exists — whoever
runs it needs to check that what they downloaded is what the server meant to
serve — so the download is never offered without it.

The card hides itself entirely when the endpoint 404s. No published build is a
normal state, not a fault, and an empty download panel would look broken. The
install command carries a `{token}` placeholder rather than a real enrolment
token: tokens are shown once, at creation, and are stored only as hashes.

## Device inventory from the agent

`GET /assets/{assetId}/device-inventory` returns the linked agent plus its latest
snapshot, and every field of it is rendered: identity, hardware, Windows, security,
network, storage, installed software and agent state. The panels live in
`features/agents/DeviceSnapshotPanels.tsx` and appear both on the asset's Device
tab and on the device's own page.

Three things shape how it is presented:

- **The agent never writes to the register.** It reports serial, manufacturer,
  model and hostname, but the asset's own fields stay as a person left them. That
  means the two can disagree — a replaced motherboard, a warranty swap, a rename —
  and the disagreement is the most useful thing on the page. `CuratedVsObserved`
  puts them side by side and flags differences, matching case-insensitively so
  `Dell Inc.` against `DELL INC.` is not reported as a discrepancy.
- **The software list is capped at 750 programs.** When a machine hits the cap the
  panel says so, because an incomplete list that looks complete is worse than no
  list.
- **What is *not* collected is stated on the page**: no signed-in users, documents,
  browser history, or product keys, and only machine-wide installs. Staff ask, and
  a short factual list answers better than an adjective.

The address a machine was last seen from is on the agent itself, as
`lastSeenIpAddress`, and shows in the devices table. It sits *outside* the signed
snapshot deliberately — it is observed by the server, so a machine cannot assert
its own source address.

## CSV import and export

Four resources import and export: **assets**, **people**, **departments** and
**locations**. Each list page carries its own Import and Export buttons.

Import is always two-phase:

1. **Validate** uploads the file and returns a job. Nothing is written. The job
   reports how many rows would be created and updated, a preview, and every
   problem with its row and column.
2. **Commit** applies it, version-checked so the same job cannot be applied twice.

`/assets/import` handles assets; `/people/import`, `/departments/import` and
`/locations/import` share one wizard driven by `referenceResources.ts`. The
shared pieces live in `ImportShared.tsx` — only the preview columns and the
wording differ.

**The `schemaVersion` column differs by resource: assets are on 2, reference data
is on 1.** Copying an asset template for a people import fails every row. The
wizard states the expected value.

Asset export streams whatever the current filters match, so "export what I'm
looking at" is literally what happens. Reference exports take no filters.

Exports are separately permissioned — `exports.assets`, `exports.people`,
`exports.departments`, `exports.locations` — so bulk person data stays
independently grantable.

Defaults are 5 MB and 500 rows, both server-configurable.

### Asset imports create reference data

An asset import whose `category` or `locationCode` does not match anything
existing **creates it** on commit, in the same transaction as the assets. A
misspelt category becomes a real category, so the review step states the counts
up front, lists the codes it is about to create, and says so before the commit
button rather than after.

`plannedCategoryCreateCount` and `plannedLocationCreateCount` are authoritative;
the codes listed come from the preview, which is capped, so the UI labels them as
a subset whenever it shows fewer than the total.

People are **not** created this way — an unknown or ambiguous person is a
validation error, because guessing who an asset belongs to is not recoverable.

### Custody is resolved, sometimes by guessing

`assignmentResolution` says how each row's owner was worked out, and the preview
shows all four cases differently because they carry different risk:

| Resolution | Meaning | How it looks |
| --- | --- | --- |
| `exactPerson` | External ID matched exactly | Name, unremarkable |
| `department` | Matched an active department | Name with a department icon |
| `fuzzyPerson` | **A guess** | Resolved name + **Guessed** badge + what the file said |
| `unassigned` | Nobody matched | Source struck through + **Not matched** |

A fuzzy match needs exactly one active candidate within a single edit, both
values at least six characters, and the first two characters unchanged. That lets
`Schappel` find `schappell` while stopping `Tgreen` becoming `Jgreen`.

**An unresolvable owner no longer fails the import.** The row is forced to
`inStock` with no custody record — the intended owner is silently dropped. That is
a real loss of information, so the review step calls it out with a count and
explains that people are never created by an asset import.

Both counts (`fuzzyPersonMatchRows`, `unassignedImportRows`) appear as warnings
before the commit button, not after.

### Dates are normalised

The parser accepts more than full calendar dates, so what lands in the register
can differ from what the file said:

| In the file | Becomes |
| --- | --- |
| `2019` (purchase, warranty start) | `2019-01-01` |
| `2022` (warranty end) | `2022-12-31` |
| `06/2021`, `06.2021` | `2021-06-01` |
| Excel serial number | the date it encodes |

Every inferred value is flagged in the preview's Dates column with an "est."
badge, and the review step counts the affected rows. Values that were already
full dates are deliberately *not* marked.

### Generated keys

Rows that arrive without an identifier get one, rather than failing:

| Resource | Missing | Becomes |
| --- | --- | --- |
| Assets | asset tag | `STR-AUTO-D99F9EAF8AF3927E-0002` |
| People | external ID | the email local-part, `j.okafor@…` → `j.okafor` |
| Departments, locations | code | a slug of the name, `Sixth Form` → `SIXTH-FORM` |

Supplied values are never replaced. Generation is **deterministic** — the same
file and row always produce the same key — so fixing other problems and
re-importing the same file with the upsert mode updates what the first import
created rather than duplicating it. The wizard flags every generated row and the
review step says how many there were.

## Domain rules the UI has to respect

These are enforced by the backend and would otherwise produce confusing `400`s:

- `assigned` status is owned by the assignment workflow. The asset form never
  offers it, and locks the status field entirely while an asset is out.
- `retired` and `disposed` cannot be set by editing an existing asset — use
  Retire, which requires a reason. They *are* allowed when creating one, so
  already-retired kit can be recorded.
- Assignment requires status `inStock` or `reserved`.
- Archiving a person needs `people.manage`; ordinary edits only need
  `people.operate`.
- Asset tags and non-empty serials are case-insensitively unique.
- **Retirement is reversible; disposal is not.** `Restore` only accepts a
  *retired* asset, so the dispose dialog asks the user to type the asset tag
  before it unlocks. Disposal is allowed from any state, including retired.
- `windows11Compliant` is tri-state — `null` means "not assessed yet", which is
  not the same as `false`, and the UI keeps them visibly distinct.
- An account cannot archive or disable **itself**, and the last enabled
  administrator cannot be removed or disabled. The accounts page warns before the
  request and explains the `409` if the server refuses anyway.
- An archived account rejects every other write until it is restored.

## Roles

`GET /api/v1/users/role-presets` is the source of truth for which roles are
assignable and what each grants. The role picker reads it, so a preset added on
the server appears without a frontend release. `src/auth/permissions.ts` only
supplies presentation — friendly names and one-line descriptions.

`InventoryOperator` was renamed **`Technician`**. The backend seeder migrates
existing assignments on startup; the frontend still labels the old name legibly if
it ever surfaces.

## Permissions

`src/auth/permissions.ts` mirrors `Permissions.cs`. Permission checks in this app
control **visibility only** — the API re-checks every one on every request. Hiding
a button is a courtesy, never a security control.

## Stocktakes, bulk changes and leavers

**Stocktakes** record what was found; they never touch the asset. Location,
condition and status stay exactly as a person left them, and the screen says so
before you start. `observedCondition` is required on every scan, so the panel
keeps it set for the run like the location rather than asking per item.

**Bulk changes** are two deliberate steps. The preview returns the exact before
and after for every asset plus a fingerprint; the commit quotes that fingerprint
back, so what is applied is provably what was shown. If any asset has been edited
in between the **whole batch** is refused — there is no partial state to reason
about afterwards, and the drawer says that before you confirm. Assets already
holding the target value are marked as no-ops and the apply button counts only
the ones that would really change.

Selection lives in `DataTable` as an optional capability, held by the caller so it
survives paging. The bulk action deliberately only sends rows from the current
page, because that is where the versions it needs come from — the button counts
what it can actually act on rather than what is ticked.

Selection can outlive the page it was made on, so the bar states both figures —
"9 assets selected · 0 on this page" — and warns in as many words that the rest
will not be changed, with Clear all beside it. The action button counts only what
it can actually act on.

**Attachments** are invoices, warranty paperwork, disposal certificates and
photographs, kept against the asset. PDF, JPEG and PNG only, up to 10 MiB, checked
before the request as well as on the server — a rejected 10 MiB upload is a slow
way to learn it was too big. Archiving is not deletion: the file drops out of the
working list but stays downloadable, which is what an auditor needs from a
disposal certificate. `attachment_malware_detected` and
`attachment_scan_unavailable` each get their own message, because "try again" is
right for one and wrong for the other.

**Bundles** group kit that goes out together. Members stay independent assets with
their own history and custody, which the editor says where someone is adding them.
Update **replaces** the member list wholesale, so the editor always sends the
complete set — a delta would silently drop everything left out.

**Leaver cases** gather what someone still holds. Opening one returns nothing,
and completion is blocked until custody is clear: the Complete button is disabled
with the reason in a tooltip rather than letting someone click into a
`leaver_has_open_custody` 409. Returning an item happens on the asset, where the
condition and notes belong — duplicating that form on the case would create a
second, thinner way to do the same thing.

## Asset tags, loans and saved searches

**Asset tags.** A tag comes from exactly one source, and the server rejects both
or neither with `asset_tag_source_invalid`. That is modelled as a union —
`{ assetTag } | { generateAssetTag: true }` — so an impossible request fails to
typecheck rather than at runtime. Only *create* can generate; an existing asset is
never retagged, so the option is hidden on an edit. The policy itself
(`/settings/asset-tags`) sits on the settings page behind `asset-tags.manage`,
with a live preview of the next tag, and says plainly that changing it leaves
existing tags alone.

**Loans.** An assignment can carry `expectedReturnOn`, a school-local calendar
date rather than an instant — "due back on the 12th" must not shift with a time
zone. `daysOverdue` and `isOverdue` come from the server and are never recomputed
here, because a browser in another zone would disagree about what day it is. A
loan with no due date is open-ended and can never be overdue, which the overdue
page states rather than leaving the reader to infer it.

**Saved searches** are private to the signed-in user by construction — no endpoint
reads or writes anyone else's. Two mismatches between the saved filter and the
list query are handled in `savedSearchFilter.ts` and covered by tests: the API
calls the free-text field `query` here and `q` on the list, and a saved filter has
no page, so restoring one always lands on page 1. A default search is applied only
when the URL carries no parameters at all, and only once per mount — otherwise
clearing the filters by hand would immediately re-apply the default and trap the
user.

## The two lockout flags

`GET /auth/me` returns `mustChangePassword` and `mfaEnrollmentRequired`. Both are
lockouts: `PermissionAuthorization` checks them before anything else and returns
**without granting any permission**, so an account in either state can sign in and
then do nothing at all.

The trap is that the same response still carries a fully populated `permissions`
array. A `can()` built from that array alone reports capabilities the server will
refuse, and the user gets a complete-looking app that answers `403` to every
click. `SessionProvider` folds both flags into `can()` in one place, and
`RequireAuth` redirects to the single screen that resolves each — in the server's
own order, password first, then `/security`. Those endpoints are `[Authorize]`
with no policy, so the escape route stays open.

## Account security and two-step verification

`/security` is self-service: an account enrols its own authenticator, sees its
recovery codes **once**, and can replace them. Enrolment is three visible steps —
confirm password, scan, enter a working code — because two-step verification only
switches on at the last step, and a half-finished enrolment that appeared to work
would lock someone out at their next sign-in.

The enrolment step renders a QR code from `authenticatorUri`, with the shared key
kept below it for anyone who cannot scan. `src/lib/qr.ts` is a self-contained
encoder (byte mode, error-correction level M, versions 1–20) rather than a
dependency or an image service — that URI *is* the TOTP secret, so it must never
leave the browser, and a QR library that renders a subtly invalid code fails in
the worst way available: it looks right, and nobody finds out until someone
cannot enrol.

`qr.test.ts` decodes the encoder's own output back to the original text and
independently recomputes the Reed–Solomon parity for every block, so a code that
would fail a real scanner's checks cannot pass the suite. `QrCode` always renders
dark-on-white with the four-module quiet zone, in both themes, because scanners
assume a light field.

The sign-in screen offers a recovery code as an alternative to an authenticator
code (`POST /auth/mfa/recovery`), since losing the phone is the usual reason for
needing it. Administrators can clear someone's second factor from the accounts
page; that account then signs in with a password alone until it enrols again, so
the confirmation says as much.

## Audit trail

`/audit` reads `GET /audit` with every filter in the URL, so a specific view can
be pasted into a ticket.

The filter **values** come from `GET /audit/vocabulary`, which reports the
distinct actions, target types and outcomes actually present in the trail. That
means the dropdowns describe this database: no option leads to an empty result,
and a new kind of event becomes filterable the first time it happens rather than
when someone next updates the frontend. `src/features/audit/auditActions.ts`
supplies only the labels and the grouping, matching each action to a group by
longest prefix so `user.mfa.` does not get swallowed by `user.`.

Four things worth knowing:

- **Unlabelled values are still offered**, under "Other", showing the raw string.
  A trail that hides entries it cannot name is worse than an ugly one.
- **A pasted URL always wins.** If it names a value the vocabulary no longer
  lists — a pre-canonicalisation spelling, or one whose rows have aged out — the
  option is added so opening the link cannot silently widen the filter.
- **The old spellings still resolve.** `6d95974` canonicalised the vocabulary
  (`stocktake.asset-observed` → `stocktake.asset.observed`, `assetImportJob` →
  `asset_import_job`, and so on) but the API still filters on the old forms, so
  the label maps keep both and an old bookmark keeps its readable names.
- **`succeeded` is the only success value written now**, but rows saying
  `success` predate the change and are immutable, so both read as a success
  forever. Either spelling in a filter matches both.

Clicking a row opens the whole record including `detailsJson`, pretty-printed but
otherwise untouched — its shape varies per action and rendering it per-shape would
eventually misrepresent one.

## School settings and server status

`/settings` writes `PUT /settings/school`. Two traps it handles explicitly:

- The **first** save must not quote a version. Until anything is persisted the
  app is running on configuration defaults, `isPersisted` is false, and the
  server rejects a request that sends a version.
- Changing **QR base URL** cannot be undone by changing it back: every label
  already printed encodes the old address, and those labels are stuck to
  equipment. The form warns as soon as the field differs from what is stored.

`/operations` reads `GET /operations/status`. Every counter there is cumulative
**since the process started**, not a rate and not an all-time total, so the page
leads with uptime — otherwise a low number reads as a quiet week rather than a
recent restart.

## Legacy cutover

`/reconciliation` runs `POST /legacy-reconciliation/dry-runs`, which compares a
legacy export against this register and produces evidence: counts, the inferences
the import would make, the issues found, sample row-by-row comparisons, and a
report fingerprint. **It writes no assets.** Committing is still done from the
asset import wizard.

Sign-off records a named decision about that evidence and is governed by three
server rules the form mirrors:

- Approving requires a written rationale for **every** blocking issue.
- A run can be signed **once**; a second attempt returns
  `reconciliation_already_signed`. A new decision needs a new dry run.
- Readiness is a three-part test — approved, `readyToCommit`, and zero error rows.
  Acknowledging a blocking issue records the exception; it does **not** make a
  failed import committable. The banner names whichever part failed rather than
  saying "not ready", and the form repeats it before you approve.

## Agent enrollment tokens

`/enrollment-tokens` issues the credential a new machine presents to enrol. The
token is returned **once**, on creation, and only its hash is stored — so the
dialog that shows it cannot be dismissed by a stray backdrop click, and offers
copy and download.

A token carries its own limits: an expiry (within the server's configured maximum,
30 days by default), a machine count, an optional hostname suffix, and whether a
TPM is required. Turning off the TPM requirement means an enrolling machine may
keep its identity key in a file that can be copied, which the form says plainly.

## Reference data editing

Categories and locations are versioned like everything else, and deactivating
either is more consequential than it looks: the server re-validates the category
and location on **every** asset write, so an asset sitting in an inactive one
cannot be saved at all until it is moved. Both editors warn about that and link to
the affected assets before you deactivate.

Locations add two tree rules, both caught before the request rather than as a
server error: a location cannot be moved inside itself or its own descendants
(`descendantsOf` in `src/features/reference/locationTree.ts` removes the subtree
from the picker), and one with active children cannot be deactivated.

## Motion

Motion here is there to say *what changed and where it came from*, not to
decorate. Everything is 80–260ms, driven by tokens in `@theme`
(`--duration-*`, `--ease-*`), and restricted to `transform` and `opacity` so it
stays off the layout path.

What moves: overlays and popovers enter and leave from their origin; toasts slide
in and out; the page settles on a route change; the nav's active bar springs in;
buttons scale slightly on press, which confirms the click before any request can
answer; dashboard tiles rise in sequence; skeletons sweep.

Two implementation notes that are easy to get wrong and are commented in place:

- `Dialog` centres itself with the CSS `translate` property, not a transform
  utility, because its keyframes animate `transform` and a running animation
  replaces that property outright.
- Dialogs and drawers close via `useDeferredClose`. They are mounted only while
  open, so a close would otherwise unmount the Radix root before it could mark
  the content `data-state="closed"` — and the exit animation would never run.

`prefers-reduced-motion` disables all of it, except elements marked
`data-motion="essential"` (the spinner and the skeleton sweep). A spinner frozen
mid-rotation says "hung", which is a worse outcome than the movement it avoids.

## Testing

Tests cover the logic that is easy to get quietly wrong: the CSRF lifecycle,
Problem Details classification, date/money formatting, the temporary-password
policy, `Button asChild` (a Radix `Slot` with more than one child throws and takes
the whole route down), `Dialog` refusing to dismiss on Escape or an outside click
when it is holding a one-time secret, the audit vocabulary and its legacy
aliases, the create-and-link payload, the QR encoder (decoded back and checked
against an independent Reed–Solomon implementation), and location-tree descendant
exclusion. Run `npm test`.

## Coverage

Every endpoint the backend implements now has a frontend surface. When a new one
lands, add its contract to `src/api`, then the route with its permission in
`src/app/navigation.ts` — never a disabled placeholder.
