# People, department, and location import/export

Status: Implemented as a post-milestone-4.1 compatibility increment.

## Workflow and authorization

Each resource uses the same persisted validate-preview-commit workflow as asset
imports:

1. Upload a CSV and an explicit `createOnly` or `upsertByKey` mode to one of:
   - `POST /api/v1/imports/people/validate`
   - `POST /api/v1/imports/departments/validate`
   - `POST /api/v1/imports/locations/validate`
2. Inspect `GET /api/v1/imports/reference-data/{id}` or download all errors from
   `GET /api/v1/imports/reference-data/{id}/errors.csv`.
3. Commit a `readyToCommit` job with its last-seen `version` at
   `POST /api/v1/imports/reference-data/{id}/commit`.

Validation persists a SHA-256-bound immutable snapshot and changes no live reference
data. Commit rechecks the job version, snapshot hash, target keys, captured record
versions, email conflicts, active custody, and the final location graph. All row
changes and the job transition use one SQLite transaction. A stale target or
late database conflict rejects the whole job and records a rejection audit.

The three import operations require `imports.validate`; job/error reads require
`imports.read`; commit requires `imports.commit`. Administrator and InventoryManager
hold those permissions. Missing `mode` is a `400` and never falls back to an implicit
default.

## Canonical version-1 schemas

Headers are case-sensitive and ordered exactly:

```text
schemaVersion,externalId,displayName,personType,email,isActive
schemaVersion,code,name,isActive
schemaVersion,code,name,type,parentCode,isActive
```

Every canonical row has `schemaVersion` value `1`.

- People use external ID as their import key. A blank external ID is generated from
  the email local-part; a row without either value is invalid. Types are `Staff`,
  `Pupil`, `Contractor`, or `Other`. Email cannot collide with a different person.
- Departments and locations use code as their import key. A blank code receives a
  deterministic uppercase, hyphen-separated code derived from the name. For example,
  `Sixth Form` becomes `SIXTH-FORM`.
- `isActive` is explicitly `true` or `false`. A person or department with open
  assignments cannot be deactivated.
- `parentCode` may refer to a live location or another row in the same file. Missing
  parents, inactive parents for active children, active children under a location
  being deactivated, self-parenting, and multi-level cycles are row errors.

`createOnly` rejects a key already present in live data. `upsertByKey` creates an
unknown key and updates a known key; it never silently matches a person by email.
The preview identifies create/update actions and whether a key was generated.

## Supplied-file compatibility

The exact headers from the three supplied source files are also accepted:

```text
displayName,personType,email
name,code
Name,Type,Code
```

Compatibility behavior is deliberately narrow:

| File | Mapping |
| --- | --- |
| `staff.csv` | Display name, person type, and email map directly; external ID is the email local-part; new rows are active |
| `departments.csv` | Name/code map directly; a blank code uses the deterministic name slug; new rows are active |
| `location.csv` | Name/type/code map directly; rows are active root locations because the source has no parent column |

No fuzzy name matching or silent deduplication occurs. Repeated keys/emails are
reported on every affected row.

## Source profile and reconciliation implications

The supplied files were profiled without checking personal row data into the
repository:

| File | SHA-256 | Data rows | Findings |
| --- | --- | ---: | --- |
| `staff.csv` | `794278D69A35ECA876436D2F560528F3A0597661CC17E4F13FC6D65207569A37` | 174 | Exact compatibility header; all rows are Staff; no blank fields, duplicate names/emails, or invalid emails |
| `departments.csv` | `4906DC9B5770DE51C1051EDF9B66C3CC40812955439B2106115168A724B24008` | 15 | Exact compatibility header; one blank code, for Sixth Form; no duplicate names/nonblank codes |
| `location.csv` | `3893229723E31C7653656EA74E9B4CD7191BFE98BE60B1E877504E643B320C6E` | 47 | Exact compatibility header; all types are Room; no blank fields or duplicate names/codes |

Deriving staff external IDs from email local-parts makes 12 of the 20 distinct
`Used By` values in the two sampled asset registers resolvable. Eight remain
unmatched and need a reconciliation decision before asset cutover. Of 94 distinct raw
asset-location values, 37 exactly match a supplied location name/code and 57 are
unmapped naming variants. Importing these reference files therefore improves legacy
resolution but does not replace milestone-5 reconciliation reports and signed
exceptions.

## Export

The canonical endpoints are:

```text
GET /api/v1/exports/people.csv
GET /api/v1/exports/departments.csv
GET /api/v1/exports/locations.csv
```

They include active and inactive rows in deterministic key order and require the
separate `exports.people`, `exports.departments`, or `exports.locations` permission.
Administrator and InventoryManager hold these permissions; Technician and
ReadOnlyAuditor do not. Keeping people export separate makes access to names and email
addresses explicit.

Exports use strict UTF-8 with a BOM, CRLF records, RFC CSV quoting, spreadsheet-formula
neutralisation, `Cache-Control: no-store`, `X-Inventory-Row-Count`, the configured row
limit, and a non-PII audit entry. Canonical exports can be revalidated with
`upsertByKey`; an unchanged round trip commits as unchanged rows.
