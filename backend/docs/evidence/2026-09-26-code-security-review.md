# Rewrite code and security review — 26 September 2026

## Scope and conclusion

Reviewed the backend, Windows endpoint agent, persistence, deployment configuration,
and relevant tests under `rewrite`, plus the rewrite CI workflow. The separate
frontend was outside this review. Repository HEAD was
`eae07a115dffdf76ef3e55cd3cb9fd05b7fe5a64`; the latest commit touching the rewrite was
`8f380c1`.

The architecture has useful security boundaries, but production sign-off is premature.
The most important new result is a reproduced authentication-lifecycle failure:
an MFA login begun before a password change can still complete afterward. The
agent's TPM requirement also verifies a client declaration, not hardware attestation.
Previously identified backup, migration, and deployment gaps remain in the code.

This was a review, not remediation. No application implementation or live inventory
data was changed. The only retained addition is this report.

Priorities: P1 means fix before depending on the affected production workflow;
P2 means a substantive security/correctness gap; P3 means maintainability or scaling
improvement. These are engineering priorities, not CVSS scores.

## Security findings

### S1 — P1: Password changes do not invalidate pending MFA logins

**Reproduced in this review using an isolated SQLite integration-test database.**

The MFA completion paths obtain the user from Identity's pending two-factor cookie
and check that the account is enabled/not archived. They do not bind that challenge
to the security stamp that existed when the password was checked.

Evidence: [IdentityService.cs:125](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:125),
[recovery completion:169](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:169),
and [password change:434](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:434).

Reproduction:

1. Enable MFA on a test administrator and retain a recovery code.
2. In a second client, submit the current password and stop at `mfa_required`.
3. In the original authenticated client, successfully change the password.
4. In the pending client, complete MFA with the recovery code.
5. Observed: MFA completion returned **204**, and a subsequent asset-list request
   returned **200**, without supplying the new password.

An attacker still needs a pending login established with the old password and a valid
second factor; this is not an anonymous authentication bypass. However, changing a
compromised password does not reliably stop a login already in progress.

The normal application-cookie stamp validator does not solve this pending-challenge
case. The pinned ASP.NET Core implementation stores pending two-factor identity
without a security stamp; its remembered-client cookie uses a different flow.
[ASP.NET Core 10.0.10 SignInManager source](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.10/src/Identity/Core/src/SignInManager.cs).

**Fix:** Bind a short-lived, single-use MFA challenge to the current security stamp
and validate it before both authenticator and recovery-code completion. Invalidate
pending challenges on password reset/change, MFA reset, and other account-security
changes. Test each transition, including disable/re-enable and role changes.

### S2 — P2: “Require TPM” trusts a client-supplied enum

**Code-confirmed; the existing agent lifecycle test demonstrates the accepted path.**

Enrollment verifies possession of the submitted ECDSA key, but the TPM policy only
checks whether `KeyProtection` equals `Tpm`. The request contains no attestation
evidence. The recovery policy has the same limitation.

Evidence: [AgentEnrollmentProfile.cs:129](S:/Inventory/rewrite/src/Inventory.Domain/Agents/AgentEnrollmentProfile.cs:129),
[AgentKeyRecoveryAuthorization.cs:128](S:/Inventory/rewrite/src/Inventory.Domain/Agents/AgentKeyRecoveryAuthorization.cs:128),
and [AgentContracts.cs:51](S:/Inventory/rewrite/src/Inventory.Contracts/V1/AgentContracts.cs:51).

The integration test creates an exportable software ECDSA key at
[AgentApiTests.cs:48](S:/Inventory/rewrite/tests/Inventory.Api.IntegrationTests/AgentApiTests.cs:48),
creates a `RequireTpm = true` profile at line 1649, reports `Tpm` at line 1681,
and expects successful enrollment. That test passed during this review.

A valid enrollment/recovery token is still required. Nevertheless, a token holder
can enroll a copyable software key while the server records TPM protection. The
official Windows agent's use of the platform provider does not prove the same
property for another client sending the protocol.

**Fix:** Either implement server-verified key attestation with an explicit trust
policy, or describe this as *reported key protection* and stop presenting it as an
enforced hardware guarantee. Do not weaken token expiry, usage limits, or signature
verification. Hardware attestation is a distinct verification step, not a provider
name or enum check.
[Microsoft TPM key-attestation guidance](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/component-updates/tpm-key-attestation).

### S3 — P2: MFA self-service verification lacks attempt controls

**Code-confirmed.** Login endpoints have a named rate limiter, but MFA enrollment,
enrollment confirmation, and recovery-code regeneration have no equivalent policy.
Their password/TOTP checks audit failure but do not increment lockout counters or
enforce a bounded challenge-attempt budget.

Evidence: [AuthController.cs:217](S:/Inventory/rewrite/src/Inventory.Api/Controllers/AuthController.cs:217),
[IdentityService.cs:243](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:243),
[confirmation:303](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:303),
and [regeneration:367](S:/Inventory/rewrite/src/Inventory.Infrastructure/Identity/IdentityService.cs:367).

These routes require an authenticated session. A compromised session can nevertheless
drive repeated password/TOTP checks and costly password hashing; the login limiter
does not protect these actions.

**Fix:** Apply per-user and source-IP throttling, bounded verification attempts,
and appropriate lockout/re-authentication semantics. Add denial/throttle regression
tests for each route, including accounts still required to enroll MFA.

### S4 — P1: Containerized ClamAV cannot refresh signatures as configured

**Previously reported; configuration and readiness code rechecked this review.**

ClamAV is attached only to the `data` network, which is `internal: true`, with no
configured updater egress, proxy, or internal signature mirror. Its readiness probe
only checks for `PONG`. Consequently a scanner can remain ready with aging signatures;
a deployment without a usable signature database can also fail initial startup.

Evidence: [compose.postgres.yml:44](S:/Inventory/rewrite/compose.postgres.yml:44),
[network:197](S:/Inventory/rewrite/compose.postgres.yml:197), and
[ClamAvAttachmentMalwareScanner.cs:32](S:/Inventory/rewrite/src/Inventory.Infrastructure/Attachments/ClamAvAttachmentMalwareScanner.cs:32).

This conclusion follows from Docker's externally isolated `internal` network and
FreshClam's need to download signature updates; it was not tested with a running
Compose stack this turn.
[Docker network semantics](https://docs.docker.com/reference/compose-file/networks/#internal),
[ClamAV signature management](https://docs.clamav.net/manual/Usage/SignatureManagement.html).

**Fix:** Provide controlled update connectivity or a trusted local mirror while
keeping the scanning port private. Readiness/monitoring should verify usable database
version and signature age, not just process responsiveness. Test fresh and stale
signature states in the actual deployment topology.

### S5 — P2: Manual reference-data changes leave an audit gap

**Previously reported; mutation paths rechecked this review.**

People, department, category, and location services save changes without recording
an actor and corresponding audit event. This prevents reliable attribution of staff
edits, reference renames, and lifecycle changes even though they affect custody and
inventory interpretation. Import auditing does not cover manual changes.

Evidence: [PeopleService.cs:66](S:/Inventory/rewrite/src/Inventory.Application/People/PeopleService.cs:66),
[DepartmentService.cs:64](S:/Inventory/rewrite/src/Inventory.Application/People/DepartmentService.cs:64),
and [ReferenceDataService.cs:240](S:/Inventory/rewrite/src/Inventory.Application/References/ReferenceDataService.cs:240).

**Fix:** Record actor, target, outcome, and a bounded before/after summary in the same
transaction as each material mutation. Retain the existing append-only audit rule and
avoid copying unnecessary personal data into audit payloads.

### S6 — P2: Repeated import previews can accumulate permanent database payloads

**Code-confirmed availability/retention gap; no storage-exhaustion test performed.**

Validation persists complete normalized rows and errors even if the import is never
committed. The endpoint has no rate limiter, and both import-job types explicitly
reject deletion. Per-file limits do not provide a cumulative user/storage budget.

Evidence: [AssetImportsController.cs:22](S:/Inventory/rewrite/src/Inventory.Api/Controllers/AssetImportsController.cs:22),
[ImportExportService.cs:627](S:/Inventory/rewrite/src/Inventory.Application/Imports/ImportExportService.cs:627),
and [InventoryDbContext.cs:1199](S:/Inventory/rewrite/src/Inventory.Infrastructure/Data/InventoryDbContext.cs:1199).

A permitted importer can repeatedly persist large previews and grow the database,
backup size, and retained personal data. The local-file free-space guard is not a
PostgreSQL-volume monitor: it returns without a local database path at
[DatabaseStorageWriteGuard.cs:27](S:/Inventory/rewrite/src/Inventory.Infrastructure/Observability/DatabaseStorageWriteGuard.cs:27).

**Fix:** Add per-user rate/concurrency and outstanding-byte/job budgets. Introduce an
explicit retention design that can expire large uncommitted preview payloads while
preserving audit evidence and required committed-job metadata. Monitor PostgreSQL
storage independently; do not simply delete append-only audit records.

## Data-integrity and deployment findings still open

These were identified previously and remain in the inspected implementation. They
were rechecked by source inspection, not rerun as fresh concurrency/deployment
experiments during this review.

### C1 — P1: SQLite backup manifest is not derived from its snapshot

[BackupRestoreService.cs:146](S:/Inventory/rewrite/src/Inventory.Infrastructure/Operations/BackupRestoreService.cs:146)
creates the backup copy, then reads migration information and row counts from the
live database at lines 154–159. Concurrent writes can make a valid snapshot disagree
with its manifest; restore rehearsal compares those counts and rejects the backup.

**Fix:** Read database evidence from the completed backup copy. Add a test that writes
to the live database after snapshot creation and proves that the backup still restores.

### C2 — P2: Location hierarchy invariants are vulnerable to cross-row races

[ReferenceDataService.cs:207](S:/Inventory/rewrite/src/Inventory.Application/References/ReferenceDataService.cs:207)
loads the hierarchy, validates it, then updates only the target row. Separate
concurrency tokens cannot serialize changes to different rows: concurrent `A → B`
and `B → A` changes can each validate against the old hierarchy. Archiving a parent
while another request attaches an active child has the same shape.

**Fix:** Serialize graph mutations or use provider-appropriate transactional locking
and revalidation. Cover API updates and imports, on SQLite and PostgreSQL, with
deterministic competing-operation tests.

### C3 — P1: CSV export/import is not a full-fidelity database cutover

The SQLite-to-PostgreSQL sequence at
[docker-postgres.md:109](S:/Inventory/rewrite/docs/operations/docker-postgres.md:109)
recommends controlled exports/imports. Those are register/reference interchange
formats, not a transfer of the complete application state. They do not preserve the
full identity/MFA, agent trust, audit/history, attachment, relationship, and key-ring
state needed to continue operating the same installation.

**Fix:** Provide and rehearse a full-state migration preserving identifiers and
relationships, attachments and hashes, identity/security data, and protection keys.
Explicitly test portability of protected data when moving from Windows to Linux.
Alternatively label CSV transfer as a deliberately limited new installation, with
clear exclusions and sign-off; do not call it a lossless cutover.

### C4 — P2: Production MSI download cannot succeed on the Linux API host

[AgentInstallerArtifactProvider.cs:183](S:/Inventory/rewrite/src/Inventory.Infrastructure/Agents/AgentInstallerArtifactProvider.cs:183)
rejects unsigned artifacts in Production, while line 193 rejects signed artifacts
on any non-Windows host. Thus supplying an otherwise valid signed MSI does not enable
the installer endpoint in the supported Linux container topology.

**Fix:** Implement a trustworthy cross-platform verification/distribution design,
such as verified release attestations with pinned trust, or a separately verified
artifact service. A manifest boolean is not a sufficient substitute for verification.

### C5 — P2: PostgreSQL restore instructions use a role the deployment does not create

[docker-postgres.md:92](S:/Inventory/rewrite/docs/operations/docker-postgres.md:92)
and line 97 use `-U inventory`, but
[create-database-roles.sh:29](S:/Inventory/rewrite/docker/postgres/create-database-roles.sh:29)
creates `inventory_owner` and `inventory_app`; both intentionally lack `CREATEDB`.
The documented restore cannot be followed against the supplied deployment unchanged.

**Fix:** Specify an appropriate operator/bootstrap identity for creating the isolated
restore database, then the appropriate owner for restore. Automate a rehearsal that
also restores the protection-key material and checks MFA, QR, history, and attachment
integrity. Keep elevated credentials out of the runtime API.

## Codebase and verification improvements

1. **Test the production topology, not just a PostgreSQL connection.**
   [rewrite-ci.yml:96](S:/Inventory/.github/workflows/rewrite-ci.yml:96) runs selected
   workflows against a PostgreSQL service and builds an image, but does not start
   the complete Compose deployment, exercise its real least-privilege roles, or
   rehearse restore. The service's `POSTGRES_USER` is the bootstrap superuser, unlike
   the production API role. Add full-stack startup/readiness, permissions, scanner,
   artifact, backup, and restore gates. Expand provider parity to rollback,
   concurrency, constraint, and authorization failures. The local PostgreSQL test
   currently returns successfully when configuration is absent unless the require
   flag is set; report an explicit skip locally and continue requiring it in CI.

2. **Move reference-list filtering/paging into database queries.**
   [ReferenceDataService.cs:27](S:/Inventory/rewrite/src/Inventory.Application/References/ReferenceDataService.cs:27)
   loads all categories before filtering/sorting/paging;
   [EfInventoryRepository.cs:384](S:/Inventory/rewrite/src/Inventory.Infrastructure/Data/EfInventoryRepository.cs:384)
   and line 409 materialize entire category/location lists. The response page cap
   therefore does not bound database reads or allocations. Introduce search-specific
   repository queries and verify ordering/case behavior on both providers.

3. **Split large orchestration classes along existing use-case boundaries.**
   `ImportExportService` is 3,124 lines, `AgentService` 2,033,
   `BackupRestoreService` 1,291, and `IdentityService` 1,113. Extract import parsing,
   reference matching, validation plans, and commit orchestration; separate agent
   protocol verification, enrollment/recovery, and fleet reads. Keep transaction
   ownership explicit. This is targeted refactoring, not a reason to replace the
   modular monolith or introduce a generic CRUD framework.

4. **Make account recovery a supported operational workflow.**
   Provide an operator-controlled, audited recovery procedure/tool rather than
   ad-hoc runners. Distinguish password reset, lockout clearance, and MFA reset;
   invalidate sessions and pending challenges deliberately. Do not silently disable
   MFA as part of a password reset.

## Controls worth preserving

- Dedicated API contracts, named permissions, and enforced project boundaries.
- CSRF protection for unsafe browser requests and secure application cookies.
- Normal-session security-stamp validation on every request.
- Signed, bounded agent ingress with freshness/replay checks and hashed enrollment
  tokens; the TPM-attestation limitation above is separate from signature validation.
- Agent telemetry separated from curated asset fields, protected local state, and a
  dedicated Windows service identity.
- Attachment signature/size checks, fail-closed scanner error handling, integrity
  checks on downloads, and attachment-oriented response headers.
- Explicit production migrations, durable key storage checks, and least-privilege
  PostgreSQL runtime-role configuration.

## Verification performed

- Built the API integration-test project and its dependencies in Release using the
  pinned SDK: **0 warnings, 0 errors**.
- Ran the existing `MfaApiTests` and `AgentApiTests`: **14 passed, 0 failed**.
- Ran an additional isolated diagnostic for S1: its expected rejection assertion
  failed, proving **204 MFA completion / 200 asset access after password change**.
  Removed the temporary diagnostic source and rebuilt afterward so it is not left
  in the test assembly.
- Queried configured NuGet vulnerability sources with
  `dotnet list Inventory.Rewrite.slnx package --vulnerable --include-transitive --no-restore`:
  **no known vulnerable packages reported** for the solution projects. This is a
  dependency advisory check, not proof that the application is secure.
- Did not run the entire solution test suite, a production Compose deployment,
  a PostgreSQL restore, a real TPM attestation experiment, or an installed-agent/GPO
  pilot in this review. No live account, password, MFA setup, or inventory record
  was changed.

## Recommended order

1. Fix pending MFA invalidation and self-service attempt controls, with regression tests.
2. Resolve the TPM trust guarantee and scanner-update/readiness deployment gap.
3. Fix snapshot manifest consistency and location-hierarchy concurrency.
4. Complete full-state cutover, restore rehearsal, and Linux MSI distribution support.
5. Close audit/retention gaps, then improve production-topology CI and targeted
   maintainability/performance issues.
