# Production release gate — fresh PostgreSQL installation

This gate is for a new single-school installation on Linux/Docker. It does **not**
authorize replacing an existing SQLite installation: CSV import does not carry
identities, MFA secrets, agent trust, attachments, history, audit, or data-protection
keys. Preserve any existing installation until its own cutover is approved.

Do not label a release production-ready because a build, unit tests, or an image
build passed. Record the exact Git revision, image digest, installer version/hash,
operator, date, and evidence for every applicable gate below. Keep secrets,
enrollment tokens, and backup contents out of this document and CI logs.

| Gate | Required evidence | Current state |
| --- | --- | --- |
| Code and contract | Release build, full test suite, formatter, both migration-model checks, OpenAPI drift check, dependency advisory review on the exact revision | On 2026-10-06, local Release tests passed 294/294; format, both migration-model checks and OpenAPI drift passed. Current solution and installer NuGet advisory checks reported no known vulnerable packages from configured sources. Rerun all gates on the final revision in CI. |
| MFA challenge lifecycle | Password/security-stamp change invalidates pending MFA; a copied pending ticket cannot complete a second session; recovery-code and TOTP paths retain normal lockout and audit behavior | Server-side five-minute, single-use challenge is implemented. Copied-cookie replay for TOTP and recovery plus expiry pass locally; concurrent completion and PostgreSQL execution remain to be exercised. |
| Location graph correctness | Deterministic competing create/reparent/deactivate tests on SQLite and PostgreSQL; one operation must fail without a cycle or active child under an inactive parent | Serializable graph mutations and a SQLite serialization test are in place; PostgreSQL write-skew test is compiled but has not run locally, and API-level competing-operation coverage remains open |
| Import-preview retention | Approved lifetime and cumulative per-user/school byte and job budgets; atomic admission, expiry, audit-preservation and concurrent-request tests on both providers | Seven-day retention, 10-job/user, 64 MiB/user and 512 MiB overall limits are implemented. SQLite tests cover job and byte admission, expiry, backfill, audit retention and signed evidence. PostgreSQL concurrent admission is now in CI but has not run locally; concurrent MFA completion and wider provider scenarios remain. |
| PostgreSQL deployment | Start the full Compose stack with production-like secrets and least-privilege `inventory_app`; prove migrations, startup, readiness, permissions, and API smoke through the HTTPS proxy | Not run on a Linux host |
| Malware scanning | FreshClam update succeeds through the approved egress or mirror; readiness rejects stale signatures; clean/infected/unavailable upload drills fail as designed | Fake-daemon tests only; topology drill pending |
| Backup and restore | Encrypted database, attachments, key ring and PFX recovery set; restore to isolated host/database; verify admin login/MFA, QR, history, attachment hashes, and audit | Host rehearsal pending |
| Signed installer | Windows release gate verifies EXE/MSI Authenticode and MSI tables; Linux API verifies independently pinned manifest signature and approved MSI digest; downloaded filename, size, checksum and metadata match | Code/tests exist; signing certificate and pilot host unavailable |
| TPM trust | Trusted issuer/service verifies cryptographic hardware attestation for the agent key; school-device pilot proves enrollment and key recovery | No issuer identified; `RequireTpm` deliberately fails closed |
| Fleet deployment | Pilot GPO/SCCM install, repair, upgrade, token expiry/replay, key loss/recovery, offline queue and revocation before broad rollout | Pending signed MSI and representative devices |
| Operations and rollback | HTTPS/proxy allow-list, secret permissions, alert delivery, storage capacity, restore/runbook ownership, rollback drill and school sign-off | Host-specific evidence pending |

The API and agent must not be rolled out as a complete hardware-trusted service
while the TPM gate is open. A backend-only pilot without agents is a separate,
explicit scope decision; it does not satisfy the agent gate.

For deployment procedures see [Docker/PostgreSQL](docker-postgres.md),
[backup and restore](backup-and-restore.md), [agent deployment](agent-deployment.md),
and [monitoring](monitoring.md).
