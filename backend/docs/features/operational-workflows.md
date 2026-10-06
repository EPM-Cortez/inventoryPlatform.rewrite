# Operational inventory workflows

Milestone 7 adds backend workflows for daily inventory control. They remain inside
the single-school modular monolith and SQLite database; there is no frontend or
separate service.

## Dashboard and exception queues

`GET /api/v1/dashboard/summary` returns total/active headlines, whole-register counts
by status and condition, and active-estate counts by category plus exception totals.
`GET /api/v1/dashboard/inventory-by-location` groups category stock by location and
separates in-stock, assigned, and other quantities. Searching for `keyboard`, for
example, reports both the total matching estate and the room/store breakdown. Retired
and disposed assets are excluded from that distribution unless the caller explicitly
asks for terminal rows.

`GET /api/v1/dashboard/exceptions/{exceptionCode}` exposes bounded asset queues for:

- `missingLocation`
- `missingSerialNumber`
- `repair`
- `lostOrStolen`
- `assignedWithoutCustody`
- `inactivePersonCustody`
- `missingWarranty`
- `expiredWarranty`

Dashboard routes require `assets.read`. Overdue assignments and leaver custody use
their dedicated queues because they include holder and due-date context.

## Physical stocktakes

A stocktake starts with one scope:

- `all` snapshots the active estate;
- `location` snapshots active assets at one active location; or
- `category` snapshots active assets in one active category.

The expected rows are immutable evidence of what the register said at start. A scan
identifies one asset by ID or tag and records an active observed location, condition,
optional note, actor, and UTC time. Exact request retries are idempotent. A changed
rescan, completion, or cancellation requires the current session version.

Progress and item filters distinguish expected, seen, missing, unexpected,
wrong-location, and condition-change rows. Completing or cancelling freezes the
session. Crucially, stocktake observations do not update asset location, condition,
status, custody, or other curated fields. Staff review discrepancies and make any
approved corrections through the existing versioned asset or custody workflows.
The before/after observation state is audited; free-text notes are represented by
hashes in that audit detail rather than copied into logs.

A session can snapshot at most 50,000 assets. The backend allows at most ten
in-progress sessions, rejects another active session with the same normalized name
and scope, checks estimated SQLite capacity before writing the snapshot, and limits
starts to two per authenticated user per minute by default.

Reads require `stocktakes.read`; start, observe, complete, and cancel require
`stocktakes.manage`.

## Managed asset tags

The singleton asset-tag policy stores a prefix, numeric width, next number, and
concurrency version. `GET` and `PUT /api/v1/settings/asset-tags` require
`asset-tags.manage`.

Asset creation must make an explicit choice:

- supply a curated `assetTag`; or
- omit the tag and set `generateAssetTag` to `true`.

Supplying both or neither is invalid. SQLite allocates the next sequence atomically,
skips existing colliding tags within a bounded search, and rolls the allocation back
if the asset transaction fails. Changing the policy never renames existing assets.
Import's stable validation-time tag reconciliation remains a separate controlled
path.

## Controlled bulk changes

Bulk change is a two-step, permission-gated workflow:

1. `POST /api/v1/assets/bulk-changes/preview` validates 1 to 200 distinct
   asset/version pairs, one operation, its references, and a required reason. It
   persists a fingerprinted preview for 30 minutes.
2. `POST /api/v1/assets/bulk-changes/{jobId}/commit` requires that preview's
   fingerprint and version, then rechecks all selected rows and references.

Supported operations are set location, category, condition, a safe non-terminal
status, or add a history note. A commit writes the job, every change, asset events,
and audit evidence atomically. One stale, missing, terminal, or otherwise invalid
asset rejects the entire batch. Assigned assets cannot have location, condition, or
status changed in bulk, and assignment, retirement, restoration, and disposal remain
dedicated workflows. Both routes require `assets.bulk`.

An identical, still-valid preview from the same user is reused rather than creating
another mutable-looking job. A user can hold at most 20 unexpired previews, and
preview creation is limited to ten requests per authenticated user per minute by
default. Committed and expired jobs remain immutable audit evidence; only unexpired
previews count toward the active cap.

## Leavers, custody, and overdue loans

An assignment can include `expectedReturnOn`. Its overdue state is derived for an
open assignment when the due date is before the current date in the persisted school
timezone.
`PUT /api/v1/assets/{assetId}/assignments/{assignmentId}/expected-return` updates or
clears the date with assignment concurrency protection.
`GET /api/v1/assignments/overdue` is a bounded queue optionally filtered by person or
department.

A leaver case names one person, leaving date, and optional notes. The case detail
returns the first 100 currently assigned assets with both assignment and asset
versions plus the full count and a truncation flag. The complete custody list is
available from the bounded
`GET /api/v1/leavers/{leaverCaseId}/assets?page=...&pageSize=...` route.
`GET /api/v1/leavers/exceptions` also surfaces people who need custody attention.
Only one open case is permitted per person.

Leaver creation, edits, cancellation, and queries never alter custody. Completion is
refused while the person still has any open assignment; staff must return or transfer
each item through the normal atomic custody endpoint. Leaver reads require
`leavers.read`; mutations require `leavers.manage`. `POST
/api/v1/leavers/{leaverCaseId}/rehire` explicitly records that a person has returned
and removes the completed/open leaver assignment block without deleting the case.

## Asset attachments

Asset attachments retain invoices, warranty documents, disposal certificates,
photos, and other evidence. The backend accepts only:

- PDF identified by its `%PDF-` signature;
- JPEG with valid leading and trailing markers; or
- PNG with its standard binary signature.

Each file must contain 1 byte to 10 MiB, have a safe filename and a matching
extension, and pass synchronous Microsoft Defender scanning before persistence.
Custom scans run with remediation disabled so the original bytes cannot be reported
clean merely because Defender removed a threat. A clean result is the only result
that permits storage; threats, ambiguous exit results, timeouts, cancellation cleanup
failures, a missing executable, and an unavailable platform all fail closed. Scanner
availability is part of `/health/ready`, and production startup rejects disabled
scanning or an invalid explicitly configured executable. The request's MIME
declaration is not trusted.

The detected content type, exact byte length, SHA-256, bytes, actor, and timestamps
are stored in SQLite. Downloads disable content sniffing and return the stored hash.
Archive is versioned and history-preserving; there is no attachment hard-delete API.
Each asset can retain at most 100 attachments and 250 MiB of attachment content.
Uploads are limited to 20 requests per authenticated user per minute by default.

Attachment list/download requires `attachments.read`; upload/archive requires
`attachments.manage`. Database capacity and backup retention must account for BLOB
growth.

## Saved searches

Saved searches are private to the authenticated user and contain a schema-versioned,
strongly validated asset filter rather than copied results. The accepted fields match
the bounded asset search contract, including category, location, custody, warranty,
date, page-size, and supported sort values.

Users may keep at most 50 active searches, names are unique per user, and one active
search can be the personal default. Archive clears default state and preserves
history. All routes live below `/api/v1/me/saved-asset-searches`, reuse `assets.read`,
and enforce owner scoping even for direct identifier lookups.

## Asset bundles

An active bundle groups 1 to 50 independently tagged assets with a role of laptop,
charger, dock, case, or a named other role. Bundle code and name are unique. A
non-terminal asset can belong to only one active bundle.

Bundle update replaces its member set using optimistic concurrency. Archive and
restore are explicit versioned actions. Membership is organisational metadata only:
it does not merge assets or propagate custody, status, condition, location, warranty,
stocktake, or lifecycle changes. Reads require `bundles.read`; mutations require
`bundles.manage`.

## SQLite backup and restore

These workflows add operational evidence that must be recovered with the database:

- tag policy and allocation sequence;
- persisted bulk previews and commit state;
- bundles and memberships;
- stocktake sessions, immutable expected rows, and observations;
- expected return dates and leaver cases;
- attachment metadata, hashes, archive state, and exact BLOB bytes; and
- saved searches and personal default state.

The online SQLite backup must include all committed WAL state. Restore rehearsal must
compare the new table counts, run integrity and foreign-key checks, and verify at
least one non-empty attachment's byte length and SHA-256 rather than checking metadata
alone. Because attachments share the database volume, operators must monitor database
and backup capacity and repeat the production-sized rehearsal after representative
evidence has been uploaded.

## Mutation boundaries

The new workflows do not create an alternate path around existing controls:

- dashboard, exception, and saved-search reads never mutate assets;
- stocktake discrepancies never correct the register automatically;
- tag-policy changes never retag existing assets;
- bulk changes cannot assign, return, retire, restore, or dispose;
- leaver cases never return or transfer equipment;
- attachment archival never deletes evidence; and
- bundle membership never changes a member asset's lifecycle or custody.

Every state-changing browser request remains authenticated, permission-gated,
antiforgery-protected, version-aware where applicable, and audited.
