# Milestone 5 release verification - 2026-07-25

## Scope

Local Windows release verification of the backend-only SQLite rewrite after the
Milestone 5 security and operations work. This evidence covers the repository and a
representative local restore rehearsal. Production storage paths, proxy addresses,
certificate/key protection, monitoring integration, production-sized data, and the
final legacy source file must still be validated on the intended host before go-live.

## Pinned toolchain

- .NET SDK `10.0.302`, selected by `global.json`;
- ASP.NET Core, EF Core, and `dotnet-ef` `10.0.10`;
- xUnit v3 `3.2.2` and Visual Studio runner `3.1.5`; and
- prerelease SDK roll-forward disabled.

## Release gates

| Gate | Result |
| --- | --- |
| `dotnet build Inventory.Rewrite.slnx -c Release --no-restore` | Passed, 0 warnings and 0 errors |
| `dotnet test Inventory.Rewrite.slnx -c Release --no-build --no-restore` | Passed, 141/141 |
| `dotnet format Inventory.Rewrite.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet-ef migrations has-pending-model-changes` | Passed, no model drift |
| `scripts/export-openapi.ps1 -Configuration Release -Verify` | Passed, no contract drift |
| `dotnet list Inventory.Rewrite.slnx package --vulnerable --include-transitive` | No vulnerable packages |
| `dotnet list Inventory.Rewrite.slnx package --deprecated --include-transitive` | No deprecated packages |

Test totals:

- API integration: 88;
- application: 18;
- architecture: 6; and
- domain: 29.

The integration project explicitly suppresses xUnit rule `xUnit1051`, which advises
passing the test-run cancellation token to every API with a compatible overload. The
suppression is test-only and does not affect production cancellation handling or test
outcomes. It can be removed incrementally as older integration helpers are updated.

## Milestone 5 evidence

- MFA enrollment, confirmation, recovery, required-role enforcement, and secret
  protection have integration coverage.
- Audit access and formula-safe bounded CSV export are permission-gated and tested.
- School settings are versioned and audited.
- account and role changes invalidate active sessions; negative authorization tests
  prove the boundary.
- production startup rejects unsafe SQLite, key-ring, backup, and forwarded-header
  configurations.
- readiness, counters, request correlation, and structured operation telemetry are
  exposed and tested.
- the online SQLite/key-ring backup and isolated restore rehearsal passed; see
  [restore rehearsal evidence](restore-rehearsal-2026-07-25.md).
- legacy reconciliation is repeatable, records immutable sign-off, keeps blocking
  import errors blocking, and generates a hashed cutover/rollback package.

## Production boundary

Before go-live, run the documented restore rehearsal against a representative
production backup, validate the real trusted proxy topology and durable local storage,
connect the operational metrics/log stream to the chosen monitor, and complete the
final legacy reconciliation sign-off. These are deployment evidence tasks, not missing
backend capabilities.
