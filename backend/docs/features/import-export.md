# Import and export

Status: Implemented in milestone 4.

This guide covers asset exchange. People, department, and location CSV workflows are
documented separately in
[`reference-data-import-export.md`](reference-data-import-export.md).

## Workflow

Asset CSV changes are deliberately split into two requests:

1. `POST /api/v1/imports/assets/validate` accepts `multipart/form-data` with a
   `file` and an explicitly supplied `mode`. Omitted mode is a `400`; there is no
   implicit create/upsert default. Validation creates an immutable snapshot and does
   not change assets, categories, or locations.
2. `GET /api/v1/imports/{id}` returns counts, a bounded valid-row preview, bounded
   inline errors, the SHA-256 file hash, generated-tag indicators, planned
   category/location creates, inferred date values, assignment-resolution details,
   fuzzy/unassigned row counts, and the import-job concurrency `version`.
3. `GET /api/v1/imports/{id}/errors.csv` returns every row/column error.
4. `POST /api/v1/imports/{id}/commit` accepts the last-seen job `version`.
   Only a `readyToCommit` job can be committed.

Commit rechecks the snapshot hash, matched identifiers, all captured concurrency
versions, active references, current custody, and primary warranty. A stale category,
location, holder, asset, assignment, warranty, tag, or serial returns `409`; the file
must be validated again.

All planned category/location creates and all asset, assignment, warranty, import-job,
event, and audit changes are sent in one EF Core `SaveChanges` transaction. A row
conflict rolls back the whole bounded job.
If the database rejects a staged write during that final save, the failed change
tracker is cleared and only a `commit_rejected` audit is saved afterward. An
integration test injects a trigger failure on the second asset and proves that the
first insert and misleading success audit are both absent.
Every changed row creates one `ImportCreated` or `ImportUpdated` asset event containing
the import job ID, CSV row, action, generated-tag indicator, and changed field names.
Unchanged upsert rows are counted but do not create misleading asset events.

## Modes

- `createOnly` rejects a tag or serial already present in live inventory.
- `upsertByAssetTag` creates an unknown tag and updates a known tag. It never matches
  by serial alone.

When `assetTag` is blank, validation generates a stable tag using
`<GeneratedAssetTagPrefix>-<first 16 characters of the file SHA-256>-<source row>`.
The generated value is stored in the immutable validation snapshot and returned with
`assetTagGenerated: true` in the preview. Revalidating an unchanged file produces the
same tags; therefore `createOnly` prevents a duplicate re-import and
`upsertByAssetTag` can safely match the earlier generated tag. A supplied tag is never
replaced.

Retired and disposed assets cannot be changed by import. The normal lifecycle actions
remain mandatory.

An imported `Assigned` row uses an active department or person when one can be
resolved. Person external IDs match exactly first. A fuzzy match is accepted only
when there is one active candidate at a single edit distance, both values are at
least six characters, and their first two characters are unchanged. This allows
`Schappel` to resolve to `schappell` without treating `Tgreen` as `jgreen`.

If the person or department is missing, inactive, ambiguous, or omitted, import does
not fail. The validated row is explicitly changed to `InStock`, creates no assignment,
and returns `assignmentResolution: unassigned` with the source value in the preview.
It is never stored as `Assigned` without an open custody record. Supplying both a
person and department, or supplying either target on a non-assigned row, remains an
error. Normal asset creation is unchanged: it still rejects `Assigned` status and
requires the dedicated assignment workflow. During upsert, a resolved declaration
can assign, transfer, preserve, or return current custody atomically. Warranty columns
create or update the active primary warranty; blank warranty columns leave existing
warranty data unchanged.

## Stable asset schema version 2

The header is case-sensitive and must use this exact order:

```text
schemaVersion,assetTag,deviceName,category,description,manufacturer,model,serialNumber,lanMacAddress,biosGuid,ownership,status,condition,locationCode,departmentCode,assignedPersonExternalId,purchaseDate,purchaseCost,currency,supplier,warrantyStartDate,warrantyEndDate,warrantyProvider,warrantyReference,operatingSystem,operatingSystemVersion,windows11Compliant,legacyId,notes
```

Rules:

- every version-2 row has `schemaVersion` value `2`;
- `category` is required; a blank `assetTag` is generated during validation;
- category accepts a unique category code or name. If it does not match an existing
  reference, validation plans a new active category and commit creates it atomically;
- location accepts a unique location code or name. If it does not match an existing
  reference, validation plans a new active location and commit creates it atomically;
- generated reference codes are uppercase name slugs of at most 50 characters.
  Deterministic hash suffixes prevent collisions with an existing or already-planned
  code. Exports always use the resolved code;
- LAN MAC addresses accept six hexadecimal octets with colon, hyphen, or no
  separators and are stored/exported as uppercase colon-separated octets;
- `biosGuid` is a GUID and `windows11Compliant` accepts `true`, `false`, `yes`,
  `no`, or blank;
- status defaults to `InStock` and condition defaults to `Good`;
- exact dates use `yyyy-MM-dd`. A year-only `yyyy` value is inferred as 1 January for
  purchase/start fields and 31 December for an end field. `MM/yyyy` and `MM.yyyy`
  similarly use the first or last day of that month;
- purchase cost is a non-negative invariant decimal with at most two places and
  requires a supported three-letter ISO 4217 currency;
- warranty end cannot precede its start, and a provider is required whenever warranty
  data is supplied;
- category, location, and department reference matches are trim-normalised and
  case-insensitive. Person external IDs additionally use the bounded, unique fuzzy
  fallback described above;
- duplicate tags and non-empty serials are detected both within the file and against
  live data;
- field lengths and the same asset/custody lifecycle rules used by normal API writes
  are enforced.

Files must be strict UTF-8 (an optional BOM is accepted) and RFC 4180-style quoted
commas, quotes, CRLF, and quoted newlines are supported. Unknown/reordered headers,
numeric enum values, invalid UTF-8, malformed quoting, extra columns, and unsupported
schema versions fail validation.

The importer continues to accept the exact schema-version-1 header and version value
for existing integrations. New exports always use version 2.

## STR legacy asset-register input

The importer also recognises the exact 20-column header used by the supplied
`STR Asset Register` CSV files:

```text
Device Name,Asset Type,Description,Serial Number,MAC(LAN),BIOS GUID,Used By,School,Location,Product,Vendor,Ownership,Cost,Warranty,Acquisition Date,Warranty Expiry Date,Asset State,OS,Os version,Windows 11 Compliant
```

| Source column | Target |
| --- | --- |
| Device Name | Nonblank values map to `assetTag` and `deviceName`; blank values receive a generated `assetTag` and retain a blank `deviceName` |
| Asset Type | Existing category code or name |
| Description | `description` |
| Serial Number | `serialNumber` |
| MAC(LAN) / BIOS GUID | `lanMacAddress` / `biosGuid` |
| Used By | Existing person's external ID and current assignment, using exact then bounded unique fuzzy matching |
| School | Required single source code; validated as one school but not copied onto assets |
| Location | Existing location code or name |
| Product / Vendor | `model` / `manufacturer` |
| Ownership | `ownership` |
| Cost | `purchaseCost`; currency comes from `LegacyDefaultCurrency` |
| Acquisition / warranty expiry | Purchase date and imported primary warranty dates |
| Asset State | `In Use` maps to `Assigned` when Used By is present, otherwise `InStock` |
| OS / Os version | `operatingSystem` / `operatingSystemVersion` |
| Windows 11 Compliant | Nullable compliance flag |

Legacy exact dates may use `dd/MM/yyyy` or `dd.MM.yyyy`. Year-only and month/year
values use the same deterministic boundary convention as version 2: purchase and
warranty start use the first possible day, while warranty end uses the last possible
day. Integer Excel serial dates are decoded using the standard OLE Automation date
system. Blank physical rows are ignored. A blank Device Name receives a deterministic
generated asset tag while `deviceName` remains blank. Blank School or more than one
school code in a file is rejected. Duplicate supplied/generated tags and serials
remain errors. Missing categories and locations are planned and created at commit. A
`Used By` value that cannot safely resolve is retained in the preview/snapshot and the
asset imports as unassigned `InStock`; people are never created implicitly from an
asset file.

## Limits and previews

Defaults are configurable under `ImportExport`:

| Setting | Default | Valid range |
| --- | ---: | ---: |
| `MaxImportBytes` | 5,242,880 | 1 KB-50 MB |
| `MaxImportRows` | 500 | 1-10,000 |
| `MaxExportRows` | 10,000 | 1-10,000 |
| `PreviewRows` | 25 | 1-200 |
| `PreviewErrors` | 100 | 1-1,000 |
| `LegacyDefaultCurrency` | GBP | Three-letter currency code |
| `GeneratedAssetTagPrefix` | STR-AUTO | 1-50 ASCII letters/digits with internal hyphens |

The byte limit is enforced from both upload metadata and the bytes actually read.
Multipart parsing has only 1 MB of additional envelope allowance. The full valid-row
snapshot and full error list are persisted even when the API preview is truncated.

## Export

`GET /api/v1/exports/assets.csv` requires `exports.assets` and accepts the asset-list
filters for search text, tag, serial, category, status, condition, location, current
department/person, assigned state, warranty state/end date, created range, legacy ID,
and stable sorting.

Exports:

- use the version-2 header and deterministic column order;
- contain current custody and active primary-warranty data;
- are UTF-8 with a BOM and CRLF record endings;
- apply RFC CSV quoting;
- prefix cells whose first non-space character is `=`, `+`, `-`, or `@` with an
  apostrophe to prevent spreadsheet formula execution;
- omit account data, QR tokens/hashes, protected QR payloads, and internal IDs;
- return `Cache-Control: no-store` and `X-Inventory-Row-Count`;
- reject an over-limit result and require narrower filters.

The importer recognises and removes the exporter-added formula guard, so an exported
file can be validated in `upsertByAssetTag` mode without changing the stored text.

Validation, successful commits, rejected commits, successful exports, and row-limit
export rejections create append-only audit entries. Import audits include planned or
created reference codes, inferred-date row counts, fuzzy-person match counts, and
unassigned import counts. Export audit details include the actor, normalized filters,
outcome, schema version, and row count or limit.
