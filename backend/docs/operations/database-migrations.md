# Database migrations

## Policy

- SQLite and PostgreSQL are supported providers with separate migration assemblies.
- Schema changes are represented only by reviewed EF Core migrations.
- The API never migrates on startup; readiness is unhealthy while migrations are
  pending.
- Back up before a production migration and apply it as a one-shot deployment step.
- Stop normal writes during migration. SQLite schema changes may rebuild tables, and
  PostgreSQL migrations can take locks.

## Apply

```powershell
dotnet ef database update `
  --project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --startup-project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --context InventoryDbContext
```

Set `ConnectionStrings__InventoryDatabase` to the exact production file before running
the command. SQLite cannot generate idempotent scripts. When a script is required,
generate it from the known currently applied migration:

```powershell
dotnet ef migrations script <CurrentMigration> <TargetMigration> `
  --project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --startup-project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --context InventoryDbContext
```

For PostgreSQL, set `Database__Provider=Postgres` and the exact connection/secret
configuration, then run the API's explicit one-shot mode:

```powershell
dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj -- --migrate
```

The Docker topology runs this command in its `migrate` service. PostgreSQL migrations
and drift checks use
`src/Inventory.Infrastructure.PostgresMigrations/Inventory.Infrastructure.PostgresMigrations.csproj`
as both project and startup project. See `docker-postgres.md`.

## Generate and validate

```powershell
dotnet ef migrations add <DescriptiveName> `
  --project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --startup-project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --context InventoryDbContext `
  --output-dir Data\Migrations

dotnet ef migrations has-pending-model-changes `
  --project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --startup-project .\src\Inventory.Infrastructure\Inventory.Infrastructure.csproj `
  --context InventoryDbContext
```

Review table names, normalized/partial unique indexes, check constraints, foreign-key
actions, table rebuilds, concurrency columns, and `Down`. Run the file-backed migration
test and the PostgreSQL compatibility workflow before release.

## Current migrations

- `InitialCreate` creates Identity, SchoolProfile, categories, locations, assets,
  events, audit records, constraints, and indexes without unsupported schemas.
- `ConfigureSqliteJournal` intentionally runs `PRAGMA journal_mode=WAL` outside a
  transaction. Its down path restores `DELETE` journal mode.
- `AddPeopleDepartmentsAndAssignments` creates holder and retained custody tables,
  target/return check constraints, concurrency columns, holder query indexes, and the
  filtered unique index that permits only one open assignment per asset. Its down path
  drops those tables and therefore loses all custody history; do not use it after live
  assignment data exists.
- `HardenCustodyInvariants` adds SQLite triggers that reject inactive assignment
  targets and prevent deactivation while open custody exists.
- `AddQrIdentifiersAndWarranties` adds retained QR and warranty tables, global token
  hash/version uniqueness, one-active-QR and one-primary-warranty filtered indexes,
  warranty/revocation check constraints, query indexes, and the one-row SchoolProfile
  trigger. Its down path deletes QR history and warranty coverage and is destructive
  after milestone 3 is used.
- `AddUserAdministrationLifecycle` adds an archive timestamp and application-managed
  user concurrency version. Existing rows receive their unique user ID as the initial
  version. Its down path preserves accounts but removes archive state and optimistic
  concurrency metadata.
- `EnforceArchivedUserDisabled` adds the database check that prevents an archived
  login from remaining enabled. SQLite rebuilds the user table to add or remove this
  constraint, so stop writes and retain the pre-migration backup.
- `AddAssetImportJobs` adds retained validation snapshots, SHA-256 file/snapshot
  hashes, validation and reconciliation counts, actor/time foreign keys, optimistic
  concurrency, status/time indexes, and database checks for consistent validation and
  commit state. Its down path removes import-job previews, errors, and reconciliation
  evidence but does not remove assets or asset events already created by a committed
  import; do not roll it back after milestone 4 is in use.
- `AddAssetDisposalLifecycle` adds nullable disposal reason and UTC timestamp columns
  to retained assets. Existing rows are unchanged. Its down path removes disposal
  evidence and must not be used after a disposal has been recorded; prefer a forward
  corrective migration.
- `AddAssetRegisterFields` adds nullable device name, description, LAN MAC, BIOS GUID,
  ownership, operating-system/version, and Windows 11 compliance columns. Existing
  assets remain valid with null values. Its down path permanently discards those
  imported attributes, so take a verified backup and prefer a forward corrective
  migration once legacy data has been loaded.
- `AddReferenceDataImportJobs` adds retained people/department/location validation
  snapshots, SHA-256 hashes, row and commit counts, actor foreign keys, concurrency,
  query indexes, and commit-state/count checks. Its down path removes reference-data
  preview, error, and reconciliation evidence but does not reverse rows already
  committed; do not roll it back after these imports are used.
- `CompleteMilestone5` adds one-to-one append-only legacy reconciliation run/sign-off
  evidence, report/source fingerprints, signed exception JSON, source/target counts,
  actor foreign keys, and unique/query indexes. Its down path permanently removes
  signed migration/cutover evidence; do not roll it back after a dry run is signed.
- `AddManagedEndpointAgents` adds usage-capped enrollment profiles, unique managed
  device/key identities, optional one-to-one asset links and append-only typed
  snapshots. It enforces profile counts/expiry, monotonic sequences, revocation state,
  per-agent snapshot/sequence uniqueness and restrictive foreign keys. Its down path
  permanently removes device trust, telemetry and enrollment evidence; revoke agents,
  retain an evidence export and prefer a forward corrective migration after rollout.
- `AddAgentReplayNonces` adds short-lived SHA-256 nonce reservations with checked
  scope/expiry, a cleanup index and compound uniqueness across request scope and
  profile/agent context. Its down path removes only active replay reservations; stop
  agent ingress while applying or rolling it back so the freshness window cannot
  straddle the schema change.
- `CompleteAgentLifecycleAndRetention` adds retained, Administrator-approved key
  recovery authorizations; key-replacement provenance; persisted short/long check-in
  capacity windows; and substantive snapshot hashes with nullable payloads for compact
  unchanged heartbeats. Existing snapshot content hashes are conservatively backfilled
  from their signed body hashes. SQLite rebuilds the agent, snapshot and nonce tables,
  so stop agent ingress for both directions. Its down path deletes null-payload
  heartbeat receipts before restoring the old non-null payload shape, removes pending
  recovery authorizations and drops the new provenance/capacity fields; take an
  evidence export and prefer a forward corrective migration after key recovery is used.
- `EnforceAgentAssetTerminalLinkInvariants` is a separate follow-up migration so its
  cross-table SQLite triggers are installed only after the preceding table rebuilds.
  The triggers reject links to retired/disposed assets and reject retirement/disposal
  while an agent remains linked. Its down path removes those last-line race guards;
  never roll it back while normal asset or agent writes are enabled.
- `AddPendingMfaChallenges` adds short-lived, single-use server-side pending-MFA
  challenges. Deployment invalidates any pre-migration pending-MFA browser cookies;
  users can sign in again. Its down path removes outstanding challenges.
- `AddImportPreviewBudgets` adds a serialized admission gate and reservations for
  asset/reference-data previews and reconciliation reports. Existing payloads are
  backfilled with seven-day expiry and signed reconciliation evidence is preserved.
  Expired unsigned payloads are cleared by the background cleanup while job, hash,
  status, and audit metadata remain. The down path removes quota/retention tracking
  but cannot restore payloads already purged; use a verified backup and a forward
  corrective migration after cleanup has run.
- `PreserveReportedSerialNumbers` adds a nullable, non-unique reported serial to
  assets. It preserves duplicate source values without weakening the unique verified
  serial index. Existing rows need no backfill. Rolling back drops the reported values
  permanently; export/reconcile them first and prefer a forward corrective migration
  once imports have used the field.

After applying `AddUserAdministrationLifecycle`, the normal serving process
transactionally reconciles role presets before accepting requests. The explicit
one-shot command remains available as a deployment preflight:

```powershell
dotnet run --project .\src\Inventory.Api\Inventory.Api.csproj -- --seed-roles
```

This is idempotent and migrates existing `InventoryOperator` memberships to
`Technician`; it does not create a login account. A real change writes
`identity.role_presets.reconciled` in the append-only audit log. The serving process
refuses pending migrations and never applies them automatically.

UTC domain timestamps are stored as converted `DateTime` text. Purchase costs are
stored as integer minor units.

## Recovery and rollback

Before live data exists, rolling back to migration `0` is acceptable. With live data,
prefer a forward corrective migration. A destructive down migration requires a verified
backup, maintenance window, and explicit data-loss approval.

EF uses `__EFMigrationsLock` while applying SQLite migrations. If a killed migration
leaves an abandoned lock, preserve a backup and confirm no migration process is active
before removing that lock table or row as described in the EF Core SQLite guidance.
