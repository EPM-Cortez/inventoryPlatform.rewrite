# Milestone 7 release verification - 2026-07-27

## Scope

Local Windows release verification of the backend-only SQLite rewrite after the
Milestone 7 operational workflows and the final security/correctness hardening pass.
This is repository acceptance evidence. Production-sized restore rehearsal, the
deployed TLS/proxy path, alert routing, organisational Authenticode signing, and
controlled GPO/SCCM pilot rings remain deployment evidence.

## Pinned toolchain

- .NET SDK `10.0.302`, selected by `global.json`;
- ASP.NET Core, EF Core, and `dotnet-ef` `10.0.10`;
- xUnit v3 `3.2.2` and Visual Studio runner `3.1.5`; and
- WiX v4 with `WixToolset.Util.wixext` `4.0.6`.

The machine-wide SDK installation was not writable from the verification session, so
the exact official SDK was invoked from `C:\Users\Jack\.dotnet\dotnet.exe`.

## Release gates

| Gate | Result |
| --- | --- |
| `dotnet build Inventory.Rewrite.slnx -c Release --no-restore` | Passed, 0 warnings and 0 errors |
| `dotnet test Inventory.Rewrite.slnx -c Release --no-build --no-restore` | Passed, 252/252 |
| `dotnet format Inventory.Rewrite.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet-ef migrations has-pending-model-changes` | Passed, no model drift |
| SQLite migration apply/downgrade and invariant tests | Passed, 6/6 |
| Full migrations against a fresh temporary SQLite database | Passed |
| `scripts/export-openapi.ps1 -Configuration Release -Verify` | Passed, no contract drift |
| Rewrite and installer dependency vulnerability scans | No vulnerable packages |
| `scripts/build-agent-msi.ps1 -Version 1.0.0` | Passed, unsigned validation MSI and manifest produced |
| `scripts/verify-agent-msi.ps1 -ExpectedVersion 1.0.0` | Passed |
| Migrated local API `/health/live` and `/health/ready` over HTTPS | Passed, both HTTP 200 |
| Live OpenAPI and CSRF bootstrap over HTTPS | Passed, OpenAPI 3.1.1 and token issued |

Test totals:

- API integration: 141;
- application: 38;
- architecture: 7;
- domain: 57; and
- Windows agent: 9.

Before migrating the existing local test database, a point-in-time copy was retained
as `src/Inventory.Api/data/backups/pre-milestone7-20260727-173238.db`. The migrated
backend was left running at `https://localhost:7270` and
`http://localhost:5292` for frontend testing.

## Closed security and correctness gaps

- Removed duplicate `EnableRateLimiting` attributes and duplicate imports that
  prevented compilation.
- Defender custom scans now use `-DisableRemediation`, interpret exit code 2 as an
  unavailable/ambiguous fail-closed result, and never store bytes after a result that
  could mean malware was remediated.
- Caller cancellation and scanner timeout terminate the Defender process tree, wait
  for bounded exit, and retry temporary-file cleanup before propagating cancellation.
- Production requires attachment scanning, validates an explicitly configured
  executable, and includes live scanner capability in `/health/ready`.
- Authentication now runs before rate limiting, allowing authenticated users behind
  one school proxy or NAT to receive independent user-keyed partitions.
- Role-preset reconciliation tests derive expected claims from the canonical
  permission set instead of a stale literal.
- A table-driven integration matrix covers all Milestone 7 read/manage permissions
  across Administrator, Inventory Manager, Technician, and Auditor presets.
- SQLite limits and race invariants protect active bulk previews, stocktakes,
  attachments, saved searches, leaver custody, bundle membership, and asset terminal
  transitions. Trigger creation occurs only after the `AssetAssignments` rebuild.
- OpenAPI includes Milestone 7 request examples and Problem Details responses, with
  the checked-in 3.1 document enforced by CI.

## Deployment boundary

Before production use, install the pinned SDK machine-wide through the approved
software channel, configure and monitor the supported malware scanner on every API
host, validate the real TLS proxy and forwarded-header topology, run a
production-sized backup/restore rehearsal, and capture signed MSI plus GPO/SCCM pilot,
supersedence, revocation, and rollback evidence.
