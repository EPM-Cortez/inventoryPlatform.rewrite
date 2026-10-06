# Changelog

## Unreleased

### Added

- Clean .NET 10 backend-only modular-monolith solution.
- Single-school asset register with categories, locations, search, concurrency,
  retirement, restoration, history, and manual notes.
- Local Identity cookie authentication, role presets, permission policies, CSRF
  protection, user administration, and first-admin bootstrap.
- File-backed SQLite EF Core schema, WAL configuration, and initial migrations.
- Optional PostgreSQL 18 runtime with a separate Npgsql migration set, database-level
  invariant triggers, least-privilege runtime role, Docker Compose deployment,
  ClamAV streaming scans, checked backups, and a real PostgreSQL CI workflow.
- Person and department holder records with case-insensitive reference uniqueness.
- Atomic person/department assignment, transfer, return, holder inventory, and
  assignment-history APIs.
- SQLite XOR and one-open-assignment constraints plus assignment concurrency tokens.
- Current custody in asset responses and person/department/assigned asset filters.
- Opaque 256-bit QR issue, SVG/PNG generation, authenticated resolution, atomic
  rotation, protected token persistence, and QR lifecycle history.
- Vector PDF asset labels for individual 62 x 29 mm roll stock and 24-up A4 sheets,
  including copies, partial-sheet start positions, bounded batches, and print audits.
- Warranty coverage CRUD-by-archive, one-primary enforcement, calculated state,
  asset summaries/filters, and expiring coverage queries.
- Persisted asset CSV validation jobs with strict UTF-8/schema parsing, reference and
  business-rule checks, previews, full error downloads, SHA-256 snapshots, and
  explicit concurrency-protected commit.
- Atomic create/upsert import reconciliation across assets, custody, primary warranty,
  import counts, events, and audit records.
- Filtered stable-schema UTF-8 CSV export with current assignment/warranty fields,
  RFC escaping, spreadsheet-formula neutralisation, bounded results, and audit records.
- OpenAPI 3.1 runtime and generated release contract, liveness/readiness endpoints,
  Problem Details, and rate-limited login.
- Domain, application, architecture, and API integration test suites.
- Runtime OpenAPI request/success examples and standard authenticated `401`/`403`
  response declarations.
- Backend product, architecture, API, development, and operations documentation.
- Explicit terminal asset disposal with separate reason/time metadata, assignment
  closure, history, and migration.
- Paged/searchable category and location detail/update APIs with optimistic
  concurrency, archive/restore, and hierarchy-cycle protection.
- Explicit import-mode binding and an injected late SQLite commit-failure test proving
  rollback plus durable rejection audit.
- Complete generated request/success/Problem Details examples and checked-in OpenAPI
  drift enforcement.
- GitHub Actions CI for restore, build, tests, formatting, EF model drift, and OpenAPI
  drift.
- Persisted, atomic people/department/location CSV imports, exact compatibility for
  the three supplied source formats, deterministic missing-key generation, canonical
  round-trip exports, dedicated bulk-export permissions, and source-profile evidence.
- Asset imports now plan missing categories and locations during read-only validation,
  create each distinct reference atomically with the assets at commit, and expose the
  planned/resolved codes in previews and audits.
- Asset imports now use bounded, unique one-edit person matching and explicitly import
  missing/inactive/ambiguous holders or departments as unassigned `InStock` assets,
  with preview and audit counts; manual asset creation remains assignment-workflow
  only.
- Asset date parsing accepts deterministic year-only and month/year values, using the
  first possible day for purchase/start fields and the last possible day for end
  fields, and decodes integer Excel serial dates from the STR compatibility format.

- Paged, filterable `GET /api/v1/audit` endpoint for holders of `audit.read`
  (action, target, actor, outcome, and occurred-at range filters, newest first).
- Paged `GET /api/v1/users` with a single batched role query.

### Security

- Trusted reverse-proxy support: `ForwardedHeaders:KnownProxies`/`KnownNetworks`
  restore real client IPs for rate limiting and audit records; unlisted sources are
  ignored.
- Database write failures are mapped to `409 Conflict` only for genuine concurrency
  and SQLite constraint violations; environmental failures (disk full, busy database)
  now surface as logged `500` errors with their root cause preserved.
- Security response headers on every response: `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, and a `frame-ancestors`
  Content-Security-Policy.
- Production startup rejects missing, wildcard, or loopback-only `AllowedHosts`.
- Label printing with `issueMissingQr` now requires `qr.generate`; print-only
  principals can no longer mint QR identifiers.
- Persisted data-protection keys are DPAPI-encrypted on Windows by default.
- `/health/ready` is rate limited per client (`RateLimits:ReadinessPermitLimit`).
- Startup-validation and must-change-password enforcement are now covered by tests.
- Directly pinned patched `Microsoft.OpenApi` and SQLite native runtime dependencies.
- Package advisories fail restore/build instead of being suppressed.
- Disabled users and security-stamp changes invalidate sessions on the next request.
- Password reset uses Identity's atomic reset operation.
- QR lookup values are SHA-256 hashes; renderable tokens are protected by the
  persistent ASP.NET Core data-protection key ring and never exposed in asset records.
