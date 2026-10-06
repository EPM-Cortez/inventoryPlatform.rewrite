# Inventory Rewrite Backend

This folder is a clean, backend-only rewrite of the school inventory system. It is a
single-school ASP.NET Core 10 modular monolith with a controller-based REST API.
The backend project remains independent of the sibling `frontend/` project. There
are no tenant, trust, or school keys on normal domain records.

The source brief is checked in at
[`docs/product/brief-pack.md`](docs/product/brief-pack.md). The current implementation
completes delivery milestones 0 through 7: the secure service foundation, asset
register and terminal lifecycle, editable reference data, people/custody workflows,
QR identifiers, warranty coverage, controlled CSV import/export, production
hardening, legacy reconciliation, enforced OpenAPI contract drift, CI, the operational
dashboard, stocktakes, tag policy, bulk changes, leaver/loan workflows, attachments,
saved searches, and bundles. It also contains the hardened Windows endpoint agent,
bounded server-side telemetry lifecycle, identity-recovery workflow, and enterprise
MSI. An estate rollout remains subject to the documented signing and pilot gates.

## What works now

- Local ASP.NET Core Identity accounts with secure cookie sessions
- Enforced administrator MFA with protected authenticator/recovery material,
  recovery login, audited reset, and forced enrolment
- CSRF protection for every state-changing API request
- Named permission policies and four role presets, including Technician
- Secure first-administrator seed plus versioned account create/read/edit,
  disable, archive/restore, role changes, and atomic password reset
- SQLite or PostgreSQL persistence with separate reviewed EF Core migrations;
  SQLite production path validation rejects temporary, deployment, network, and
  removable storage
- Paged categories and hierarchical locations with versioned archive/restore
- Asset create/read/search/update plus explicit retire/restore/dispose workflows
- Full operational register attributes for device name/description, LAN MAC, BIOS GUID,
  ownership, OS/version, and Windows 11 compliance
- Separate people and department holder records with paged search and archival updates
- Atomic assign, transfer, return, and retirement custody closure
- Expected return dates, school-timezone overdue-loan reporting, and leaver cases that
  list outstanding custody without silently returning equipment
- Current-holder asset filters, holder inventory views, and complete custody history
- Dashboard status/condition/category totals, category/location stock distribution,
  and bounded exception queues
- Immutable-snapshot physical stocktakes with idempotent observations and
  missing/unexpected/location/condition discrepancy reporting
- Versioned managed asset-tag policy with atomic opt-in tag generation
- Persisted 30-minute bulk previews with fingerprinted, all-or-nothing commits that
  cannot bypass custody or terminal lifecycle workflows
- Versioned laptop/charger/dock/case bundles with one-active-bundle membership
- Signature-validated, SHA-256-hashed PDF/JPEG/PNG asset evidence up to 10 MiB,
  archive-only lifecycle, and database backup coverage
- Private validated saved asset filters with one personal default
- Opaque QR issue, SVG/PNG rendering, authenticated resolution, and atomic rotation
- Print-ready individual 62 x 29 mm and 24-up A4 QR labels with partial-sheet batching
- Warranty add/update/archive, primary coverage summaries, state filters, and
  expiring-soon reports
- Strict UTF-8 CSV validation jobs with previews, downloadable row/column errors,
  SHA-256 snapshots, stable tag generation for blank import tags, explicit versioned
  commit, planned creation of missing categories/locations, deterministic partial-date
  normalization, atomic create/upsert reconciliation, seven-day preview retention,
  and shared cumulative job/byte budgets
- Filtered stable-schema asset CSV export with current custody/warranty data,
  spreadsheet-formula neutralisation, round-trip tests, limits, and audit records
- Persisted people, department, and location CSV validation/commit jobs plus
  permission-separated canonical exports and exact adapters for the three supplied
  reference files
- Backward-compatible canonical schema v1 input plus schema v2 and direct validation of
  the sampled 20-column STR asset-register format
- Case-insensitive asset-tag uniqueness and duplicate serial protection
- Application-managed optimistic concurrency with `409 Conflict`
- Asset retire/restore/dispose and append-only readable asset history
- Manual asset history notes
- Security/administrative audit records with paged search and bounded,
  formula-safe `audit.export`
- Versioned, audited administration of the single school profile
- Immediate cookie invalidation after account, role, password, MFA, archive, or
  restore changes
- Trusted-proxy allow-listing, correlated structured request logs, operational
  counters/alerts, storage/backup age reporting, and storage-aware readiness
- Online SQLite plus data-protection-key backups with SHA-256 manifests and an
  isolated restore rehearsal covering integrity, migrations, counts, login, audit,
  asset history, and active QR unprotection
- Deterministic legacy dry runs with duplicate/unmapped reports, source/target
  counts, sampled comparisons, immutable sign-off, and cutover/rollback packages
- Signed Windows endpoint enrollment/check-ins with reported key protection
  (`RequireTpm` remains fail-closed until hardware attestation is verified),
  replay defence, protected offline queueing, typed bounded telemetry and audited
  exact/manual asset linking
- Audited one-time agent identity recovery after key loss or reimage, immediate
  revocation unlinking, terminal-asset link guards, per-agent ingress quotas, compact
  unchanged receipts, and bounded detailed-snapshot retention
- Backend-only agent management with fleet search/freshness filters, exact summary
  counts, link/unlink, dismiss/restore review, revocation, and atomic asset creation
- Optional manager-only MSI metadata/download with per-request hash validation,
  streaming delivery, Windows Authenticode verification, and pinned signed-release
  attestation for Linux production hosts
- A least-privileged self-contained Windows service plus major-upgrade MSI for silent
  GPO/SCCM deployment, with ordered offline delivery, jittered retries, server cadence,
  repair-safe properties, and legacy-package collision protection; production signing
  and estate pilots remain explicit gates
- OpenAPI 3.1 in Development with examples, Problem Details errors, and CI drift checks
- Separate liveness and database readiness probes
- Domain, application, architecture, security, migration, and API integration tests

## Prerequisites

- .NET SDK `10.0.302` (selected by `global.json`)
- Either a writable local filesystem directory for SQLite and backups, or Docker with
  PostgreSQL 18 and the documented secret files
- Windows 10/11 x64 with TPM 2.0 for the default endpoint-agent deployment

The bundled SQLite native library remains the default for development, tests, and
single-host production, so no database server is required. Development defaults to
`src/Inventory.Api/data/inventory.db`. Production requires an absolute path on local
durable storage:

```powershell
$env:ConnectionStrings__InventoryDatabase = 'Data Source=D:\InventoryData\inventory.db;Foreign Keys=True;Default Timeout=30;Pooling=True'
$env:Security__DataProtectionKeysPath = 'D:\InventoryData\keys'
$env:Storage__BackupPath = 'E:\InventoryBackups'
$env:School__QrBaseUrl = 'https://inventory.example.school/scan'
$env:AllowedHosts = 'inventory.example.school'
```

Run one write-capable API instance, keep the database off network shares, and protect
the database volume with operating-system encryption and restrictive filesystem
permissions. SQLite itself is not encrypted by this application.

For container hosting, `compose.postgres.yml` runs the API with PostgreSQL 18 and a
ClamAV sidecar on a private network. It applies the separate PostgreSQL migrations in
an explicit one-shot container before starting the API. See
[`docs/operations/docker-postgres.md`](docs/operations/docker-postgres.md).

## Restore, build, and test

Run these commands from this `rewrite` directory:

```powershell
dotnet restore .\Inventory.Rewrite.slnx
dotnet tool restore
dotnet build .\Inventory.Rewrite.slnx --no-restore
dotnet test .\Inventory.Rewrite.slnx --no-build --no-restore
```

Package vulnerability warnings are treated as build failures. Dependency versions are
centrally pinned in `Directory.Packages.props`; the EF tool is pinned in
`.config/dotnet-tools.json`.

Regenerate the checked-in OpenAPI release contract from the running application:

```powershell
.\scripts\export-openapi.ps1
```

Verify without changing the checked-in contract:

```powershell
.\scripts\export-openapi.ps1 -Configuration Release -Verify
```

Build an unsigned development MSI:

```powershell
.\scripts\build-agent-package.ps1 -Version '1.0.0'
.\scripts\verify-agent-msi.ps1 -ExpectedVersion '1.0.0'
```

Production packaging must pass `-CertificateThumbprint` and `-RequireSignature`.
Deployment properties, one-time token staging, GPO assignment, SCCM
detection/supersedence and pilot checks are in
[`docs/operations/agent-deployment.md`](docs/operations/agent-deployment.md).

## Create or update the database

Migrations are an explicit operator step. The web process never creates or migrates a
production database on startup.

```powershell
dotnet ef database update `
  --project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --startup-project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --context InventoryDbContext
```

The migration creates the database and enables SQLite WAL mode. SQLite cannot generate
idempotent migration scripts, so apply a known migration range or use
`dotnet ef database update`.

PostgreSQL uses its own migration assembly:

```powershell
$env:Database__Provider = 'Postgres'
$env:ConnectionStrings__InventoryDatabase = 'Host=localhost;Port=5432;Database=inventory;Username=inventory;Password=<secret>'

dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj -- --migrate
```

Do not start the serving process with pending migrations. Selecting PostgreSQL does
not copy an existing SQLite database.

## Create the first administrator

Apply the migration first. The interactive helper keeps a default password out of
source and prompts without echoing:

```powershell
.\scripts\seed-admin.ps1 `
  -Email 'admin@example.school' `
  -DisplayName 'System Administrator'
```

For non-interactive deployment automation, supply the same values through the
deployment secret store:

```powershell
$env:BootstrapAdmin__Email = 'admin@example.school'
$env:BootstrapAdmin__DisplayName = 'System Administrator'
$env:BootstrapAdmin__Password = '<strong unique seed password>'

dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj -- --seed-admin

Remove-Item Env:BootstrapAdmin__Password
```

The seed is idempotent only for the same administrator and refuses a different account
after an enabled administrator exists. No default credential is committed. Use the
authenticated user API after first setup.

Normal API startup now reconciles the code-managed role presets before accepting
requests. It refuses to serve with pending migrations, and a repair is transactional
and audited. The explicit command remains useful as an upgrade preflight and also
migrates former `InventoryOperator` assignments to `Technician`:

```powershell
dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj -- --seed-roles
```

## Back up and rehearse restore

For SQLite, configure absolute database, key-ring, and backup paths, then create an
online snapshot. The SQLite backup API folds committed WAL state into the standalone
backup database:

```powershell
.\scripts\backup-inventory.ps1
```

Rehearse the latest backup against an enabled test login. The password is prompted
without echo and is not written to the report:

```powershell
.\scripts\rehearse-restore.ps1 `
  -LoginEmail 'restore-check@example.school'
```

The rehearsal never replaces the live database. It restores into an isolated
directory, writes a JSON evidence report below the backup root, then removes the
temporary restored copy. See
[`docs/operations/backup-and-restore.md`](docs/operations/backup-and-restore.md).
PostgreSQL uses `pg_dump`/`pg_restore`; the Docker operations profile writes a
checksum manifest and the container guide includes an isolated restore rehearsal.

## Run the API

```powershell
dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj
```

In Development:

- OpenAPI JSON: `/openapi/v1.json`
- Liveness: `/health/live`
- Readiness: `/health/ready`

Cookie-authenticated clients must first call `GET /api/v1/auth/csrf`, retain the
antiforgery cookie, and send the returned token as `X-CSRF-TOKEN` on every unsafe
request. Fetch a new token after login because the authenticated identity has changed.

## Solution map

```text
src/
  Inventory.Agent/           Self-contained signed Windows inventory service
  Inventory.Domain/          Pure entities and business rules
  Inventory.Application/     Use cases, ports, permissions, application models
  Inventory.Infrastructure/  EF Core, Identity, provider-neutral persistence
  Inventory.Infrastructure.PostgresMigrations/  PostgreSQL migrations/invariants
  Inventory.Contracts/       Versioned HTTP request/response contracts
  Inventory.Api/             Controllers, auth pipeline, OpenAPI, health
installer/
  Inventory.Agent/           WiX major-upgrade MSI
tests/
  Inventory.Domain.Tests/
  Inventory.Application.Tests/
  Inventory.Api.IntegrationTests/
  Inventory.Architecture.Tests/
```

See [`docs/index.md`](docs/index.md) for the full documentation map and
[`docs/product/acceptance-criteria.md`](docs/product/acceptance-criteria.md) for the
implemented/planned boundary. Daily dashboard, stocktake, tag, bulk, leaver/loan,
attachment, saved-search, and bundle semantics are consolidated in
[`docs/features/operational-workflows.md`](docs/features/operational-workflows.md).
