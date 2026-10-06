# Milestone 6.1 release verification - 2026-07-26

## Scope

Local Windows release verification of the backend-only SQLite rewrite after the
Milestone 6.1 backend, agent-lifecycle, scheduling, retention, and MSI hardening.
This is repository acceptance evidence. Organisational Authenticode signing,
representative physical Windows/TPM lifecycle tests, the deployed TLS/proxy path,
production-sized restore rehearsal, and controlled GPO/SCCM pilot rings remain
deployment evidence.

## Pinned toolchain

- .NET SDK `10.0.302`, selected by `global.json`;
- ASP.NET Core, EF Core, and `dotnet-ef` `10.0.10`;
- xUnit v3 `3.2.2` and Visual Studio runner `3.1.5`; and
- WiX v4 with `WixToolset.Util.wixext` `4.0.6`.

## Release gates

| Gate | Result |
| --- | --- |
| `dotnet build Inventory.Rewrite.slnx -c Release --no-restore` | Passed, 0 warnings and 0 errors |
| `dotnet test Inventory.Rewrite.slnx -c Release --no-build --no-restore` | Passed, 195/195 |
| `dotnet format Inventory.Rewrite.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet-ef migrations has-pending-model-changes` | Passed, no model drift |
| SQLite migration apply/downgrade integration tests | Passed, 5/5 |
| `scripts/export-openapi.ps1 -Configuration Release -Verify` | Passed, no contract drift |
| Rewrite and installer dependency vulnerability scans | No vulnerable packages |
| `scripts/build-agent-msi.ps1 -Version 1.0.0` | Passed, unsigned validation MSI and manifest produced |
| `scripts/verify-agent-msi.ps1 -ExpectedVersion 1.0.0` | Passed |
| Migrated local API `/health/live` and `/health/ready` over HTTPS | Passed, `200 Healthy` |

Test totals:

- API integration: 120;
- application: 20;
- architecture: 7;
- domain: 39; and
- Windows agent: 9.

## Closed security and correctness gaps

- Production startup requires enabled Administrator MFA in both early and resolved
  options validation. The configured required-role list is authoritative rather than
  silently padded by an object default.
- QR path tokens are redacted from request telemetry, and self-service password
  changes and administrator resets use separate per-IP rate-limit partitions.
- Warranty business dates use the persisted school timezone, including daylight
  saving boundaries.
- Readiness and operational status distinguish database-volume and backup-volume
  capacity.
- Backup manifests and restore comparison cover all 27 critical tables, including
  Identity roles, claims, assignments, logins, protected tokens, agent recovery,
  nonce, enrollment, receipt, settings, import, and legacy stores. The isolated
  rehearsal also proves that the restored Administrator retains the current
  permission preset and that protected MFA material can be unprotected with the
  restored key ring.
- Agent recovery uses one-time expiring hashed authorizations, immediately revokes the
  old key, unlinks the asset, preserves device sequence identity, and requires an
  explicit audited relink.
- Service checks and SQLite triggers prevent linking agents to retired/disposed
  assets or entering a terminal asset state while an agent remains linked.
- Check-in ingress is bounded per agent while allowing configured offline backlog
  drain. Unchanged snapshots retain compact receipts rather than duplicate payloads,
  and detailed payload retention is bounded by count and bytes.

## Agent and MSI evidence

- The offline queue is strict FIFO: a backed-off or failed head item blocks newer
  sequences, `Retry-After` is honoured, and server sequence/cadence values are
  validated before persisted state changes.
- Enrollment and check-in retries use bounded exponential backoff and stable
  per-device jitter; server-advertised cadence persists across service restarts.
- State schema v2 migrates existing v1 state without losing enrollment or queued
  snapshots.
- MSI repair and major upgrade preserve API URL, staged-token path, and software-key
  policy.
- The package detects the legacy UpgradeCode, Windows service, and install marker and
  blocks unsafe side-by-side ownership. The obsolete standalone agent workflow is
  retired; the rewrite CI publishes the MSI with its matching artifact manifest.

## Deployment boundary

Before broad rollout, sign the MSI and binaries with the organisation's trusted
certificate, validate install/reboot/repair/upgrade/uninstall on representative
Windows 10/11 TPM hardware, exercise the real proxy and TLS termination path, run a
production-sized backup/restore rehearsal, and capture GPO/SCCM pilot, supersedence,
revocation, and rollback evidence.
