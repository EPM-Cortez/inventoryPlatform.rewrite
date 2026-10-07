# API examples

The examples omit cookie storage syntax because clients differ. Preserve both cookies
and refresh the CSRF token after login.

## Obtain CSRF token

```http
GET /api/v1/auth/csrf HTTP/1.1
Host: inventory.example.school
Accept: application/json
```

```json
{ "token": "<request token>" }
```

## Login

```http
POST /api/v1/auth/login HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <anonymous request token>

{
  "email": "admin@example.school",
  "password": "<secret>",
  "rememberMe": false
}
```

Success is `204 No Content`. Fetch a new CSRF token after the authentication cookie is
issued.

## Create, edit, archive, and restore a login account

People and departments are custody records. Login accounts are administered separately
and require `users.manage`. Retrieve the assignable presets from
`GET /api/v1/users/role-presets`; the day-to-day operational preset is `Technician`.

```http
POST /api/v1/users HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "email": "technician@example.school",
  "displayName": "Inventory Technician",
  "temporaryPassword": "<unique temporary password>",
  "roles": ["Technician"]
}
```

The account must change its temporary password. Copy the returned `version` when
editing any account field or role:

```http
PUT /api/v1/users/{userId} HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "email": "technician@example.school",
  "displayName": "Senior Inventory Technician",
  "isEnabled": true,
  "roles": ["Technician"],
  "version": "<last-seen user version>"
}
```

Deletion is a history-preserving archive. It disables login, invalidates existing
sessions, and retains audit/event authorship:

```http
DELETE /api/v1/users/{userId}?version=<last-seen-user-version>
X-CSRF-TOKEN: <authenticated request token>
```

Restore using the new version returned by `GET /api/v1/users/{userId}`:

```http
POST /api/v1/users/{userId}/restore
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{ "version": "<archived user version>" }
```

Stale versions, self-archive, and removal or disablement of the final enabled
administrator return `409 Conflict`.

## Create category and asset

```http
POST /api/v1/categories HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "code": "LAP",
  "name": "Laptop",
  "description": "Portable computers"
}
```

```http
POST /api/v1/assets HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "assetTag": "SCH-000123",
  "categoryId": "11111111-1111-1111-1111-111111111111",
  "manufacturer": "Dell",
  "model": "Latitude 5450",
  "serialNumber": "ABC12345",
  "status": "inStock",
  "condition": "good",
  "locationId": null,
  "purchaseDate": "2026-07-20",
  "purchaseCost": 899.00,
  "currency": "GBP",
  "supplier": "Example Supplier",
  "notes": null,
  "legacyId": "OLD-8842"
}
```

Success is `201 Created`, with `Location` pointing to the new asset.

To use the centrally managed tag sequence, omit `assetTag` and opt in explicitly:

```http
POST /api/v1/assets HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "generateAssetTag": true,
  "categoryId": "11111111-1111-1111-1111-111111111111",
  "deviceName": "ICT-LT-043",
  "manufacturer": "Dell",
  "model": "Latitude 5450",
  "status": "inStock",
  "condition": "good"
}
```

Supplying both `assetTag` and `generateAssetTag: true`, or supplying neither, returns
`400`. Generation consumes the next available value atomically and skips an existing
colliding tag. Administrators and inventory managers can inspect and version-update
the singleton policy:

```http
GET /api/v1/settings/asset-tags
```

```json
{
  "prefix": "STR-",
  "numericWidth": 6,
  "nextNumber": 124,
  "nextAssetTag": "STR-000124",
  "version": "a891d01c-8f11-4e5d-85c6-9b6f02ef137b"
}
```

```http
PUT /api/v1/settings/asset-tags
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "prefix": "STR-",
  "numericWidth": 6,
  "nextNumber": 5000,
  "version": "a891d01c-8f11-4e5d-85c6-9b6f02ef137b"
}
```

## Update with concurrency

Copy every mutable field and the last-seen `version`:

```http
PUT /api/v1/assets/f4dd9d0d-787e-4a52-9670-52b3bf17d079 HTTP/1.1
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "assetTag": "SCH-000123",
  "categoryId": "11111111-1111-1111-1111-111111111111",
  "manufacturer": "Dell",
  "model": "Latitude 5450",
  "serialNumber": "ABC12345",
  "status": "repair",
  "condition": "damaged",
  "locationId": null,
  "purchaseDate": "2026-07-20",
  "purchaseCost": 899.00,
  "currency": "GBP",
  "supplier": "Example Supplier",
  "notes": "Awaiting inspection",
  "legacyId": "OLD-8842",
  "version": "a891d01c-8f11-4e5d-85c6-9b6f02ef137b"
}
```

A stale version returns:

```json
{
  "type": "https://inventory.local/problems/concurrency-conflict",
  "title": "The record was changed by another user.",
  "status": 409,
  "detail": "The asset was changed by another user.",
  "instance": "/api/v1/assets/f4dd9d0d-787e-4a52-9670-52b3bf17d079",
  "traceId": "..."
}
```

## Retire, restore, and dispose

```http
POST /api/v1/assets/{assetId}/retire
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "version": "<last-seen version>",
  "reason": "End of useful life"
}
```

```http
POST /api/v1/assets/{assetId}/restore
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<retired asset version>" }
```

Disposal is terminal and records metadata separately from retirement:

```http
POST /api/v1/assets/{assetId}/dispose
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "version": "<last-seen version>",
  "reason": "Transferred to certified recycler"
}
```

The response contains `disposedAtUtc` and `disposalReason`. A disposed asset cannot be
restored or edited through the normal update endpoint. If an endpoint agent is linked,
retire and dispose both return `409 asset_has_linked_agent`; unlink or revoke the agent
first, obtain the asset's current version, and retry.

## Edit or archive reference data

Category and location lists are paged (`pageSize` maximum 200) and support `q` plus
`includeInactive`. Creation returns a `Location` header. Use the returned version for
updates; set `isActive` to `false` to archive and back to `true` to restore:

```http
PUT /api/v1/categories/{categoryId}
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "code": "LAP",
  "name": "Laptops",
  "description": "Portable computers",
  "isActive": false,
  "version": "<last-seen category version>"
}
```

Active location parents cannot be deactivated while they have active children, and
parent changes that would create a hierarchy cycle return `400`.

## Search and history

```http
GET /api/v1/assets?q=patel&assigned=true&page=1&pageSize=50&sort=assetTag
```

```http
GET /api/v1/assets/{assetId}/history?page=1&pageSize=50
```

## Dashboard, stock distribution, and exception queues

The dashboard returns total/active headlines, status and condition counts for the
whole register, and bounded category/exception counts over the active estate:

```http
GET /api/v1/dashboard/summary?categoryLimit=1
```

```json
{
  "totalAssets": 932,
  "activeAssets": 917,
  "statusCounts": [
    { "value": "inStock", "count": 248 },
    { "value": "reserved", "count": 40 },
    { "value": "assigned", "count": 621 },
    { "value": "repair", "count": 8 },
    { "value": "retired", "count": 12 },
    { "value": "disposed", "count": 3 }
  ],
  "conditionCounts": [
    { "value": "new", "count": 16 },
    { "value": "good", "count": 801 },
    { "value": "fair", "count": 80 },
    { "value": "damaged", "count": 35 }
  ],
  "categoryCounts": [
    {
      "categoryId": "11111111-1111-1111-1111-111111111111",
      "categoryCode": "KEY",
      "categoryName": "Keyboard",
      "count": 86
    }
  ],
  "categoryCountsTruncated": true,
  "exceptionCounts": [
    {
      "code": "missingLocation",
      "label": "Assets without a recorded location",
      "count": 12
    }
  ]
}
```

To answer “how many keyboards do we have, and where are they stored?”, search the
category distribution:

```http
GET /api/v1/dashboard/inventory-by-location?query=keyboard&page=1&pageSize=50
```

Each group reports `quantity`, `inStockQuantity`, `assignedQuantity`, and
`otherQuantity` for one category/location pair. Terminal assets are excluded unless
`includeTerminalAssets=true`.

Supported drill-down exception codes are `missingLocation`, `missingSerialNumber`,
`repair`, `lostOrStolen`, `assignedWithoutCustody`, `inactivePersonCustody`,
`missingWarranty`, and `expiredWarranty`:

```http
GET /api/v1/dashboard/exceptions/inactivePersonCustody?page=1&pageSize=50
```

Overdue loans and leaver custody are separate operational queues described below.

## Physical stocktake

Starting a stocktake takes an immutable snapshot of the expected assets. Use `all`,
`location`, or `category`; supply only the identifier required by that scope:

```http
POST /api/v1/stocktakes
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "name": "Summer keyboard check",
  "scope": "category",
  "categoryId": "11111111-1111-1111-1111-111111111111",
  "locationId": null
}
```

Record a scan by exactly one of `assetId` or `assetTag`. An active observed location
and a condition are required:

```http
POST /api/v1/stocktakes/{sessionId}/observations
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "assetId": null,
  "assetTag": "STR-000124",
  "observedLocationId": "66666666-6666-6666-6666-666666666666",
  "observedCondition": "good",
  "note": "Found in the locked ICT store",
  "version": "<last-seen stocktake version>"
}
```

An exact retry is idempotent and returns `changed: false`, even if it carries the
pre-retry version. A changed rescan needs the current version. Filter the immutable
expected snapshot plus observations with `all`, `seen`, `missing`, `unexpected`,
`wrongLocation`, or `conditionChanged`:

```http
GET /api/v1/stocktakes/{sessionId}/items?filter=missing&page=1&pageSize=50
```

Close with the last-seen version:

```http
POST /api/v1/stocktakes/{sessionId}/complete
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen stocktake version>" }
```

Cancelling uses the same version plus a required reason. Neither observations nor
completion change an asset's curated status, condition, or location; discrepancies
must be resolved through the normal asset/custody workflows.

One session can snapshot at most 50,000 assets. At most ten sessions may remain in
progress, another active session with the same normalized name and scope returns
`409 stocktake_duplicate_active`, and starts are limited to two per authenticated
user per minute by default.

## Controlled bulk changes

Bulk operations accept 1 to 200 distinct asset/version pairs and exactly one safe
operation: `setLocation`, `setCategory`, `setCondition`, `setStatus`, or `addNote`.
A reason is mandatory:

```http
POST /api/v1/assets/bulk-changes/preview
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "assets": [
    {
      "assetId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "version": "11111111-2222-3333-4444-555555555555"
    },
    {
      "assetId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "version": "66666666-7777-8888-9999-000000000000"
    }
  ],
  "operation": {
    "kind": "setLocation",
    "locationId": "66666666-6666-6666-6666-666666666666",
    "clearLocation": false
  },
  "reason": "Moved into the refurbished ICT store"
}
```

The `201` preview is persisted for 30 minutes and returns item-level before/after
values, a `jobId`, `fingerprint`, expiry, and version. Commit exactly that snapshot:

```http
POST /api/v1/assets/bulk-changes/{jobId}/commit
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "fingerprint": "<preview fingerprint>",
  "version": "<preview version>"
}
```

Commit rechecks every reference and asset version and writes the asset changes,
history, job state, and audit record in one transaction. Any stale or invalid item
rejects the whole batch. Retired/disposed assets are not mutable; assigned assets
cannot have location, condition, or status changed this way; and `assigned`,
`retired`, and `disposed` are not bulk-settable statuses.

Repeating an identical valid preview as the same user returns the existing job.
Each user can retain at most 20 unexpired previews, with ten preview requests per
authenticated user per minute by default. Expired and committed jobs remain
immutable audit evidence but do not consume the active-preview allowance.

## Saved searches and personal default

Saved searches store validated asset-list filters, not result snapshots:

```http
POST /api/v1/me/saved-asset-searches
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "name": "ICT keyboards in stock",
  "schemaVersion": 1,
  "filter": {
    "query": "keyboard",
    "locationId": "66666666-6666-6666-6666-666666666666",
    "status": "inStock",
    "pageSize": 50,
    "sort": "assetTag"
  }
}
```

Each user can retain at most 50 active searches with unique names and at most one
default. Set the default with the returned version:

```http
POST /api/v1/me/saved-asset-searches/{savedSearchId}/default
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen saved-search version>" }
```

`GET /api/v1/me/saved-asset-searches/default` returns `204` when none is selected.
Archive is history-preserving. All list, detail, update, default, and archive
operations are scoped to the signed-in user.

## Create holders

People are custody records, not login users:

```http
POST /api/v1/people
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "externalId": "STF-1042",
  "displayName": "A. Patel",
  "type": "staff",
  "email": "a.patel@example.school"
}
```

```http
POST /api/v1/departments
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "code": "ICT",
  "name": "ICT Services"
}
```

## Assign or transfer

Supply exactly one target and the asset version from the latest asset response:

```http
POST /api/v1/assets/{assetId}/assignments
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "personId": "22222222-2222-2222-2222-222222222222",
  "departmentId": null,
  "assetVersion": "33333333-3333-3333-3333-333333333333",
  "notes": "Issued with charger",
  "expectedReturnOn": "2026-09-30"
}
```

Posting again with a different person or department and the newly returned
`assetVersion` performs an atomic transfer. The response contains the new assignment,
assignment version, asset status, and new asset version.

Change or clear the due date without altering custody:

```http
PUT /api/v1/assets/{assetId}/assignments/{assignmentId}/expected-return
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "expectedReturnOn": "2026-10-14",
  "assignmentVersion": "<last-seen assignment version>"
}
```

Overdue means an open assignment whose expected return date is before “today” in the
persisted school timezone:

```http
GET /api/v1/assignments/overdue?personId={personId}&page=1&pageSize=50
```

## Return

Use both last-seen versions. Optional condition and location values are applied to the
asset as part of the same transaction:

```http
POST /api/v1/assets/{assetId}/assignments/{assignmentId}/return
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "assetVersion": "44444444-4444-4444-4444-444444444444",
  "assignmentVersion": "55555555-5555-5555-5555-555555555555",
  "condition": "damaged",
  "locationId": "66666666-6666-6666-6666-666666666666",
  "notes": "Screen cracked"
}
```

A stale asset or assignment version returns `409` without persisting any part of the
return.

## Leaver and custody exceptions

Open a case against an existing person:

```http
POST /api/v1/leavers
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "personId": "22222222-2222-2222-2222-222222222222",
  "leavingOn": "2026-08-31",
  "notes": "Recover equipment before final working day"
}
```

The response previews the first 100 current assets, includes the full count and a
truncation flag, and carries assignment/asset versions, expected-return dates, and
overdue flags. A person can have only one open case. Creation and updates never
return, transfer, or otherwise mutate custody.

```http
GET /api/v1/leavers?status=open&q=patel&page=1&pageSize=50
GET /api/v1/leavers/exceptions?page=1&pageSize=50
GET /api/v1/leavers/{leaverCaseId}
GET /api/v1/leavers/{leaverCaseId}/assets?page=1&pageSize=200
```

Complete only after returning or transferring every asset through the normal custody
endpoint:

```http
POST /api/v1/leavers/{leaverCaseId}/complete
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen leaver-case version>" }
```

Open custody returns `409 leaver_has_open_custody`. Cancelling closes the case without
changing custody.

If the person returns, explicitly mark the case as rehired using its current version:

```http
POST /api/v1/leavers/{leaverCaseId}/rehire
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen leaver-case version>" }
```

This retains the case history and removes the leaver assignment block; it does not
assign or return any asset.

## Asset bundles

A bundle groups independently tagged assets for operational handling:

```http
POST /api/v1/asset-bundles
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "code": "KIT-0042",
  "name": "Staff laptop kit 42",
  "description": "Laptop with its issued accessories",
  "members": [
    {
      "assetId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "role": "laptop"
    },
    {
      "assetId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "role": "charger"
    },
    {
      "assetId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "role": "dock"
    },
    {
      "assetId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
      "role": "case"
    }
  ]
}
```

Codes and names are unique, a bundle contains 1 to 50 distinct non-terminal assets,
and an asset can belong to only one active bundle. `other` requires an `otherRole`.
Update replaces the membership using the last-seen bundle version. Archive/restore
are explicit versioned actions; membership never combines or propagates a member's
custody, lifecycle, location, or audit history.

## Asset evidence attachments

Upload one bounded evidence file using multipart form data:

```http
POST /api/v1/assets/{assetId}/attachments
Content-Type: multipart/form-data; boundary=<boundary>
X-CSRF-TOKEN: <token>

--<boundary>
Content-Disposition: form-data; name="category"

invoice
--<boundary>
Content-Disposition: form-data; name="file"; filename="invoice-2026-0042.pdf"
Content-Type: application/pdf

<PDF bytes>
--<boundary>--
```

Categories are `invoice`, `warrantyDocument`, `disposalCertificate`, `photo`, and
`other`. The backend accepts only signature-validated PDF, JPEG, or PNG content up to
10 MiB, rejects unsafe filenames and extension/content mismatches, and requires a
clean synchronous Microsoft Defender custom scan before calculating SHA-256 and
storing the bytes in SQLite. The submitted MIME header is not trusted. Threats return
`409 attachment_malware_detected`; ambiguous, timed-out, cancelled, or unavailable
scans return `409 attachment_scan_unavailable`, and no bytes are persisted.

Each asset can hold at most 100 attachments and 250 MiB of attachment content.
Uploads are limited to 20 per authenticated user per minute by default. Production
requires an available scanner, and scanner readiness participates in
`GET /health/ready`.

```http
GET /api/v1/assets/{assetId}/attachments?includeArchived=false&page=1&pageSize=50
GET /api/v1/assets/{assetId}/attachments/{attachmentId}/download
```

Downloads send the detected content type, `X-Content-Type-Options: nosniff`, and
`X-Content-SHA256`. Archive with the last-seen attachment version:

```http
POST /api/v1/assets/{assetId}/attachments/{attachmentId}/archive
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen attachment version>" }
```

There is no hard-delete endpoint. The database BLOB, metadata, content hash, archive
state, and authorship form part of the database backup/restore recovery set.

## Issue, render, resolve, and rotate QR

Issue an asset's first QR identifier:

```http
POST /api/v1/assets/{assetId}/qr
X-CSRF-TOKEN: <token>
```

The `201` response includes `payloadUrl`, `imageUrl`, and `tokenVersion`. Keep the
payload URL inside the authorised label workflow; do not put it in logs or exports.

```http
GET /api/v1/assets/{assetId}/qr
GET /api/v1/assets/{assetId}/qr?format=png
```

After a client decodes the HTTPS payload, use its last path segment:

```http
GET /api/v1/qr/<opaque-token>
```

This requires authentication and `assets.read`. It returns a limited asset/category,
location, status, condition, and warranty summary. It never returns serial, purchase,
holder, token hash, or protected token fields.

```http
POST /api/v1/assets/{assetId}/qr/rotate
X-CSRF-TOKEN: <token>
```

Rotation requires `qr.rotate`. The previous token immediately returns the same
non-sensitive `404` as an unknown token.

## Print individual or batch QR labels

Print one 62 x 29 mm roll label:

```http
POST /api/v1/assets/{assetId}/label
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "stock": "brotherDk11209",
  "copies": 1,
  "startPosition": 1,
  "issueMissingQr": true
}
```

Print an A4 Avery L7159 sheet, beginning at the seventh unused label:

```http
POST /api/v1/labels/batch
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "stock": "averyL7159",
  "startPosition": 7,
  "issueMissingQr": true,
  "items": [
    {
      "assetId": "11111111-1111-1111-1111-111111111111",
      "quantity": 1
    },
    {
      "assetId": "22222222-2222-2222-2222-222222222222",
      "quantity": 2
    }
  ]
}
```

Both endpoints return `application/pdf`. `GET /api/v1/labels/stocks` lists supported
stock dimensions. Print at actual size/100%; details and limits are in
`docs/features/qr-label-printing.md`.

## Add and query warranty coverage

```http
POST /api/v1/assets/{assetId}/warranties
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "provider": "Example Warranty Ltd",
  "coverageType": "manufacturer",
  "startDate": "2026-09-01",
  "endDate": "2029-08-31",
  "reference": "WAR-12345",
  "notes": null,
  "isPrimary": true
}
```

Copy the returned `version` into `PUT /api/v1/warranties/{warrantyId}` or
`POST /api/v1/warranties/{warrantyId}/archive`. Making another coverage primary
demotes the earlier primary.

```http
GET /api/v1/assets/{assetId}/warranties
GET /api/v1/warranties/expiring?withinDays=60&page=1&pageSize=50
GET /api/v1/assets?warrantyState=expiringSoon&warrantyEndsBefore=2026-09-30
```

## Validate, inspect, and commit an asset CSV

Use the exact schema documented in `docs/features/import-export.md`. Validation is a
multipart request and creates no assets:

```http
POST /api/v1/imports/assets/validate
Content-Type: multipart/form-data; boundary=<boundary>
X-CSRF-TOKEN: <token>

--<boundary>
Content-Disposition: form-data; name="mode"

CreateOnly
--<boundary>
Content-Disposition: form-data; name="file"; filename="assets.csv"
Content-Type: text/csv

schemaVersion,assetTag,deviceName,category,description,manufacturer,model,serialNumber,lanMacAddress,biosGuid,ownership,status,condition,locationCode,departmentCode,assignedPersonExternalId,purchaseDate,purchaseCost,currency,supplier,warrantyStartDate,warrantyEndDate,warrantyProvider,warrantyReference,operatingSystem,operatingSystemVersion,windows11Compliant,legacyId,notes
2,SCH-000124,STR-ICT-LPT-24,LAP,ICT support laptop,Dell,Latitude 5450,ABC12346,74:86:E2:3A:00:96,4c4c4544-0030-3310-8043-c3c04f475233,Leased,InStock,Good,ICT,,,2026-07-20,899.00,GBP,Example Supplier,,,,Windows,11,true,OLD-8843,
--<boundary>--
```

Both parts are required. Omitting `mode` returns `400`; clients must deliberately
choose `createOnly` or `upsertByAssetTag`.

An imported row may leave `assetTag` blank. Validation assigns a stable tag and returns
it in the preview with `assetTagGenerated: true`; the source `deviceName`, if any, is
not changed.

An unmatched category or location is returned in the preview with
`categoryWillBeCreated` or `locationWillBeCreated` and its generated
`resolvedCategoryCode` or `resolvedLocationCode`. Validation remains read-only; commit
creates each distinct planned reference once in the same transaction as the assets.
Year-only dates are normalized to 1 January for purchase/start fields and 31 December
for end fields. The preview returns the selected dates and `inferredDateFields`.

Person targets use exact external-ID matching first, followed by a bounded unique
one-edit match. For example, `Schappel` can resolve to `schappell`. The preview returns
the original `assignmentSource`, the resolved display name, and
`assignmentResolution: fuzzyPerson`. If a person or department is no longer present,
inactive, ambiguous, or omitted from an `Assigned` import row, validation instead
returns `assignmentResolution: unassigned`, changes that row to `InStock`, and permits
commit without creating a custody record. This exception applies only to imports;
manual asset creation still rejects `Assigned` status.

The `201` job response reports `readyToCommit` or `validationFailed`, counts including
`generatedAssetTagRows`, `plannedCategoryCreateCount`,
`plannedLocationCreateCount`, `inferredDateRows`, `fuzzyPersonMatchRows`,
`unassignedImportRows`, `reportedDuplicateSerialRows`, preview rows with
`sourceSerialNumber`/`serialResolution`, errors, `fileHash`, and `version`.
Duplicate serials are retained as `reportedSerialNumber` on the asset rather
than inserted into the unique verified `serialNumber` field. Retrieve the job or its
full error report:

```http
GET /api/v1/imports/{importJobId}
GET /api/v1/imports/{importJobId}/errors.csv
```

Asset and people/department/location validation share a persisted preview budget:
10 active jobs per user, 64 MiB per user and 512 MiB overall by default. A full
budget returns `409 import_preview_budget_exceeded` without creating a job. Raw
preview and row-error payloads expire after seven days; reads and commits then
return `409 import_preview_expired`. The job identity, counts, hashes and audit
history remain. Successful ordinary commits purge their row payloads and release
quota immediately; when a correction attempt reaches the budget, the oldest
failed previews for that same user are superseded. Ready jobs and reconciliation
evidence are not automatically evicted. Signed legacy reconciliation evidence is
preserved, but an
expired, uncommitted import must be validated again before cutover.

Commit only the last-seen ready job version:

```http
POST /api/v1/imports/{importJobId}/commit
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen import job version>" }
```

A stale reference, changed asset/custody/warranty, duplicate identifier introduced
after validation, wrong version, or repeated commit returns `409`. No row is committed
from a rejected job. A successful response also reports `createdCategoryCount` and
`createdLocationCount`.

## Export asset CSV

Export accepts the asset-list filters and includes current custody and primary
warranty fields:

```http
GET /api/v1/exports/assets.csv?status=assigned&departmentId=<departmentId>&sort=assetTag
```

The response is a stable schema-version-2 UTF-8 CSV with a BOM,
`Cache-Control: no-store`, and `X-Inventory-Row-Count`. Cells that could execute as
spreadsheet formulas are neutralised. Use `upsertByAssetTag` to validate a controlled
round trip.

## Import and export people, departments, and locations

Upload the appropriate canonical or supported compatibility CSV with an explicit mode:

```http
POST /api/v1/imports/people/validate
Content-Type: multipart/form-data; boundary=<boundary>
X-CSRF-TOKEN: <token>

--<boundary>
Content-Disposition: form-data; name="mode"

CreateOnly
--<boundary>
Content-Disposition: form-data; name="file"; filename="staff.csv"
Content-Type: text/csv

displayName,personType,email
Jane Smith,Staff,j.smith@example.school
--<boundary>--
```

Use the same request shape with `/departments/validate` or `/locations/validate`.
Inspect and commit the persisted resource-typed job:

```http
GET /api/v1/imports/reference-data/{importJobId}
GET /api/v1/imports/reference-data/{importJobId}/errors.csv

POST /api/v1/imports/reference-data/{importJobId}/commit
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "version": "<last-seen import job version>" }
```

The people preview reports derived key `j.smith` with `keyGenerated: true`. A blank
department code for `Sixth Form` similarly previews `SIXTH-FORM`. Commit rechecks
person emails, open assignments, target versions, and the complete location graph;
any stale conflict returns `409` without a partial write.

Download canonical, active-and-inactive reference data with:

```http
GET /api/v1/exports/people.csv
GET /api/v1/exports/departments.csv
GET /api/v1/exports/locations.csv
```

These endpoints use separate bulk-export permissions and return formula-safe UTF-8
CSV with a BOM, `Cache-Control: no-store`, and `X-Inventory-Row-Count`.

## Endpoint-agent management

Search and filter on the server. Trust and connectivity are separate filters:

```http
GET /api/v1/agents?q=ICT-LT&trustStatus=active&matchState=unmatched&connectivity=stale&page=1&pageSize=50
GET /api/v1/agents/summary
```

The list reports `matchReason`, `connectivityStatus`,
`nextExpectedCheckInAtUtc`, `overdueAtUtc`, enrolment-profile provenance, linked
asset details, and revocation provenance. Use the latest returned `version` for every
management action:

```http
POST /api/v1/agents/{agentId}/dismiss
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "reason": "Known disposable examination image",
  "version": "<last-seen agent version>"
}
```

Restore the row with `/restore-review`, clear an incorrect link with `/unlink`, or
link an existing asset with `/link`. Dismissal is not revocation and blocks automatic
matching until review is restored.

Create a missing curated asset and link it atomically:

```http
POST /api/v1/agents/{agentId}/create-and-link-asset
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "assetTag": "STR-012345",
  "deviceName": "ICT-LT-042",
  "categoryId": "<categoryId>",
  "manufacturer": "Dell",
  "model": "Latitude 5450",
  "serialNumber": "ABC1234",
  "status": "inStock",
  "condition": "good",
  "reason": "No curated asset existed for the enrolled endpoint",
  "version": "<last-seen agent version>"
}
```

This route requires both `agents.manage` and `assets.create`. It never copies observed
telemetry implicitly; the submitted values are the curated record. A failed asset,
event, link, or audit write rolls back all four.

Recover a lost/replaced device key only from an Administrator session:

```http
POST /api/v1/agents/{agentId}/key-recovery
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "reason": "TPM was cleared during motherboard replacement",
  "expiresAtUtc": "2026-07-26T22:30:00Z",
  "requireTpm": false,
  "version": "<last-seen agent version>"
}
```

The `201` response returns `recoveryToken` once with `Cache-Control: no-store`.
Approval immediately rejects the old signing key and releases any linked asset. Stage
the token through the protected enrollment-token file or the hidden direct-token MSI
bootstrap used for initial enrollment; the replacement agent signs the ordinary
`/api/v1/agent/enroll` request with its new key. A successful replacement preserves
`nextSequence`, consumes the token, and requires an explicit asset relink.

List outstanding authorizations, or include closed history needed for revoke/status
surfaces:

```http
GET /api/v1/agents/{agentId}/key-recovery
GET /api/v1/agents/{agentId}/key-recovery?includeClosed=true
```

The paged result includes `authorizationId`, `status` (`pending`, `expired`,
`consumed`, or `revoked`), reason, approver, expiry, closure metadata, and `version`.
It never includes `recoveryToken`, its hash, or the consumed key thumbprint.
Cancel an unused authorization with:

```http
POST /api/v1/agents/{agentId}/key-recovery/{authorizationId}/revoke
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "reason": "Repair was cancelled and the staged token was withdrawn",
  "version": "<authorization version>"
}
```

When the optional signed installer paths are configured, managers can inspect and
stream the verified artifact:

```http
GET /api/v1/agents/installer
GET /api/v1/agents/installer/download
```

The metadata contains filename, version, bytes, SHA-256, verification state, download URL,
and an `ENROLLMENT_TOKEN=<one-time-enrollment-token>` command template. It never
contains a real enrolment token. Windows Production rejects MSI files that fail
Authenticode verification. Linux Production requires a separately trusted signed
release manifest and an approved MSI digest; it does not inspect the MSI's
embedded Authenticode signature.
On Linux, `releaseAttestationVerified: true` means the signed release manifest and
approved MSI digest were verified. The legacy `signed` field remains `false` because
the container has not directly verified the MSI's Authenticode signature; `signer`
begins with `Release attestation:`. On Windows, `signed: true` means the MSI's
Authenticode signature was verified.

## Administrator MFA

Begin enrolment with the signed-in account's current password:

```http
POST /api/v1/auth/mfa/enrollment
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "currentPassword": "<current password>" }
```

Confirm using the authenticator's six-digit code:

```http
POST /api/v1/auth/mfa/enrollment/confirm
Content-Type: application/json
X-CSRF-TOKEN: <token>

{ "code": "123456" }
```

The response displays recovery codes once with `Cache-Control: no-store`. A later
password login returns an MFA challenge completed through
`POST /api/v1/auth/login/mfa`; a recovery code instead uses
`POST /api/v1/auth/login/mfa/recovery`. Regenerate codes only with current password
and TOTP. Administrators can invalidate another account's MFA through the versioned,
audited `POST /api/v1/users/{userId}/reset-mfa` endpoint.

## Audit export and school settings

Audit export uses the search filters and never includes `DetailsJson`:

```http
GET /api/v1/audit/vocabulary
GET /api/v1/audit/export?action=user.roles.changed&from=2026-07-01T00:00:00Z
```

The vocabulary response supplies canonical, sorted filter values currently present
in the audit store, capped at 200 per field with a `truncated` indicator.
The response is formula-safe CSV with `X-Inventory-Row-Count`,
`X-Inventory-Truncated`, and `Cache-Control: no-store`.

Create the single school profile without a version, then use its returned version for
subsequent updates:

```http
PUT /api/v1/settings/school
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "name": "Example School",
  "code": "EXS",
  "timeZone": "Europe/London",
  "qrBaseUrl": "https://inventory.example.school/scan",
  "warrantyWarningDays": 60,
  "version": null
}
```

## Legacy reconciliation and cutover evidence

Create a dry run by posting the same `file` and explicit `mode` multipart fields used
by asset validation:

```http
POST /api/v1/legacy-reconciliation/dry-runs
Content-Type: multipart/form-data; boundary=<boundary>
X-CSRF-TOKEN: <token>
```

The persisted report includes source/target counts, duplicate/unmapped and mapping
issues, deterministic samples, and `reportFingerprint`. Sign a zero-error report:

```http
POST /api/v1/legacy-reconciliation/{importJobId}/sign-off
Content-Type: application/json
X-CSRF-TOKEN: <token>

{
  "decision": "approved",
  "reportFingerprint": "<64-character SHA-256>",
  "notes": "Counts, mappings, samples, and rollback reviewed.",
  "exceptions": []
}
```

A report with blocking issues requires every stable issue key plus a rationale, but
remains non-committable. Download the reproducible evidence:

```http
GET /api/v1/legacy-reconciliation/{importJobId}/cutover-package
```

`cutoverReady` is false until an approved report has no validation errors and the
versioned import remains ready and unexpired (or has already committed). Signed
exceptions never bypass the normal import commit gate.
