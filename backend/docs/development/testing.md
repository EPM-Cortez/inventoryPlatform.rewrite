# Testing

Run from `rewrite`:

```powershell
dotnet restore .\Inventory.Rewrite.slnx
dotnet tool restore
dotnet build .\Inventory.Rewrite.slnx --no-restore
dotnet test .\Inventory.Rewrite.slnx --no-build --no-restore
dotnet format .\Inventory.Rewrite.slnx --verify-no-changes --no-restore
dotnet list .\Inventory.Rewrite.slnx package --vulnerable --include-transitive
.\scripts\export-openapi.ps1 -Configuration Release -Verify
```

Current suites cover domain invariants, application orchestration, architecture
boundaries, cookie/CSRF/permission behaviour, disabled-session revocation, asset
persistence, uniqueness, concurrency, people/departments, assignment target/status
rules, atomic stale-return rollback, transfer history, holder queries, and the OpenAPI
version. Milestone 3 adds all warranty-state boundaries, invalid date ranges, stale
warranty updates, primary replacement/archive, warranty filters/reports, QR image
formats, opaque token storage, authorization, resolution, revocation, and rotation
history. The API test decodes the generated PNG with the test-only ZXing.Net core
package and requires the exact issued HTTPS payload.

Milestone 4.1 adds terminal-disposal and reference-lifecycle tests, explicit multipart
mode binding, complete OpenAPI example/error assertions, and a real late-transaction
test. That test installs a narrow SQLite trigger which rejects the second imported
asset during `SaveChanges`; it verifies rollback of the earlier insert and success
audit, preservation of the ready job, and a durable rejection audit.

The main API suite uses SQLite in memory; the migration suite also creates a temporary
file-backed database and verifies WAL mode, foreign keys, applied migrations,
assignment, QR, and warranty check/partial indexes, and production path guards. Do not
replace migrations with `EnsureCreated`.

`PostgresCompatibilityTests` resets only a database whose name ends in `_test`, applies
the PostgreSQL migrations, and runs representative asset, custody, bundle, stocktake,
reference import/export, leaver, attachment, saved-search, and signed-agent workflows.
CI provisions PostgreSQL 18 and requires this test; a missing connection cannot silently
skip there.

Every new endpoint needs happy-path coverage and applicable `400`, `401`, `403`,
`404`, and `409` cases. Every migration needs a clean file-backed apply test and
reviewed forward/rollback notes.

The root GitHub Actions workflow runs the same restore, Release build/test, formatting,
dual-provider pending-model-change, PostgreSQL compatibility, container-build, and
OpenAPI-drift gates on every rewrite change.
