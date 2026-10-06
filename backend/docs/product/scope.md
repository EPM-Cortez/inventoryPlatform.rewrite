# Product scope

## Product statement

A secure single-school inventory API that answers what an asset is, where it is, its
current state, and what material lifecycle changes occurred.

## Implemented phase

- One deployable ASP.NET Core service and one file-backed SQLite database
- Local accounts, cookie sessions, permission policies, and user administration
- Paged category and hierarchical-location search, detail, versioned update, archive,
  and restore
- Asset register, search/filter/page, update, concurrency, retire/restore, and
  terminal disposal
- Register device name/description, LAN MAC, BIOS GUID, ownership, OS/version, and
  Windows 11 compliance attributes
- People and department holder records, search, versioned update, and archive state
- Atomic assign/transfer/return, expected-return dates, overdue-loan queues, current
  custody, and complete assignment history
- Leaver cases with current-custody recovery lists and a completion gate that requires
  all of the person's assignments to be closed
- Dashboard summaries, category/location stock distribution, and bounded operational
  exception queues
- Physical all/location/category stocktake sessions with immutable expected snapshots,
  idempotent observations, discrepancy reports, and no implicit register mutation
- Versioned managed asset-tag policy with explicit atomic generation on asset creation
- Persisted preview/fingerprint/commit bulk changes with all-or-nothing asset history
  and custody/lifecycle bypass prevention
- Versioned asset bundles for laptop/charger/dock/case kits while preserving each
  member's independent lifecycle and custody
- Signature-validated, hashed, archive-only PDF/JPEG/PNG asset evidence stored in
  SQLite and included in backup/restore validation
- Private, schema-versioned saved asset filters with one personal default
- Opaque QR identifiers with SVG/PNG rendering, authenticated resolution, and rotation
- Print-ready individual roll labels and 24-up A4 QR label sheets
- Warranty coverage, primary summaries, calculated states, filters, and expiry report
- Persisted CSV validate/preview/error/commit jobs with atomic asset, custody,
  warranty, history, and reconciliation changes
- Filtered stable UTF-8 CSV export with formula neutralisation and audit records
- Canonical CSV schema v2, backward-compatible v1 input, and the sampled STR legacy
  register adapter with explicit row-level data-quality failures
- Canonical people/department/location import and export, exact supplied-file
  compatibility, generated missing reference keys, and hierarchy/custody revalidation
- Append-only asset history and security audit records
- Administrator MFA, bounded audit export, versioned school settings, immediate
  role-session invalidation, and transactionally coupled identity/audit writes
- Trusted-proxy and durable-storage validation, correlated operational telemetry,
  backup/storage readiness, and online backup/isolated restore commands
- Persisted deterministic legacy reconciliation, signed exceptions, sampled
  comparisons, and cutover/rollback evidence packages
- OpenAPI 3.1 with request/success/error examples, runtime Problem Details, checked-in
  contract drift enforcement, and operational health endpoints
- Windows CI for restore, build, tests, formatting, EF migration drift, and OpenAPI
  contract drift

## Deployment acceptance outside repository scope

- Repeat the measured restore rehearsal with production-sized data, the production
  service identity/certificate, and production storage.
- Complete the final source-specific reconciliation dry run/sign-off and retain its
  clean cutover package for the actual legacy dataset.

## Non-goals

- Frontend design or implementation
- Multi-school/trust tenancy or school switching
- Procurement, depreciation, helpdesk, MDM/RMM, or remote control
- Public asset details or public registration
- Email/SMS notification workflows

Use *assignment* or *custody*, never legal ownership, for a person or department holding
school equipment.
