# Milestone plan

## Delivery status

Milestones 0 through 5 are implemented in this repository. A post-4.1 asset-register
compatibility increment added the fields and bounded CSV adapter needed for the two
sampled STR registers. Milestone 4.1 closed its review findings before production
hardening:

- terminal disposal is an explicit, versioned asset action with separate reason/time
  metadata, custody closure, and immutable history;
- category and location reference data supports paged search, detail, versioned edit,
  archive, and restore without hard deletion;
- CSV validation requires an explicit import mode;
- an injected failure during the final SQLite save proves whole-job rollback and a
  separately persisted rejection audit;
- every structured request/success response and every documented error has a generated
  example, errors use Problem Details, and conditional permissions are described;
- the generated OpenAPI file is drift-checked; and
- GitHub Actions runs restore, build, tests, format validation, EF model-drift
  validation, OpenAPI drift validation, and dependency checks.

The compatibility increment adds device name, description, LAN MAC, BIOS GUID,
ownership, OS/version, and Windows 11 compliance to the register/API/labels; exports
canonical schema version 2 while accepting version 1; and validates the supplied
20-column STR format with deterministic missing tags and partial-date normalisation.
The reviewed sample counts and cleanup gates are recorded in
[`str-register-sample-analysis.md`](str-register-sample-analysis.md).

A second post-4.1 increment adds persisted, atomic people, department, and location
imports plus permission-separated canonical exports. It accepts the three supplied
reference-file headers, derives staff external IDs from email local-parts, generates
blank department/location codes deterministically, and preserves location hierarchy
rules. Asset imports also plan missing categories and locations during validation,
then create them in the same transaction as the assets; people remain an explicit
identity import and are never inferred from an asset file. Source profiles are in
[`reference-data-import-export.md`](../features/reference-data-import-export.md).

## Milestone 5: migration and production hardening

Milestone 5 is implemented with the following repository evidence:

1. **MFA** — configured roles are restricted until enrolment; authenticator and
   recovery material is protected at rest; TOTP, one-time recovery login,
   regeneration, administrator reset, audit, and negative tests are present.
2. **Audit access and export** — permission-scoped search and bounded, formula-safe
   CSV export omit structured details and secrets.
3. **School settings** — the one-row profile has validated, versioned, audited
   administration.
4. **Proxy and storage validation** — forwarding is explicitly enabled with a trusted
   proxy/network; production startup rejects relative, temporary, deployment,
   network, removable, nested, or unwritable database/key/backup paths. Exports remain
   streamed, so there is no persistent attachment/export store.
5. **Role-session invalidation** — zero-interval stamp revalidation and positive/
   negative integration tests prove old cookies lose changed permissions immediately.
6. **Backup/restore rehearsal** — one-shot commands use SQLite online backup, copy the
   key ring, write hashes/migrations/counts, and rehearse in isolation. The measured
   WAL/DPAPI drill verifies integrity, foreign keys, migrations, login, QR, asset
   history, audit, and rollback.
7. **Observability** — correlation headers/logs, aggregate counters, threshold alerts,
   storage capacity, backup age, and storage-aware readiness are exposed to
   administrators and tested.
8. **Legacy reconciliation** — atomic dry runs persist deterministic fingerprints,
   duplicate/unmapped and mapping-decision issues, source/target counts, samples,
   immutable authenticated sign-offs, and reproducible cutover/rollback packages.
   Signed exceptions remain non-bypassing evidence.

Repository exit evidence is under `docs/evidence/milestone-5`; CI reruns build, tests,
format, EF model-drift, OpenAPI drift, and dependency checks. Each real deployment
must repeat the restore rehearsal with its production-sized database, service
identity, certificate/key protection, and storage before go-live.

## Milestone 6: secure Windows endpoint agent foundation

The backend and Windows-service foundation is implemented:

1. **Least-privileged service** — a self-contained .NET 10 Windows Worker runs under
   the unique `NT SERVICE\InventoryEndpointAgent` virtual account rather than
   LocalSystem.
2. **Machine identity** — a non-exportable CNG ECDSA P-256 key uses the TPM provider
   by default; software-key fallback requires explicit client and server permission.
3. **Enrollment and replay defence** — short-lived, usage-capped, revocable profiles
   issue a token once; signed requests bind timestamps, fresh nonces, body hashes and
   monotonic device sequences.
4. **Protected offline operation** — DPAPI-protected atomic state queues bounded typed
   snapshots and re-signs each retry with fresh freshness data.
5. **Inventory integration** — append-only telemetry covers hardware, OS, security,
   network, fixed storage and machine-wide installed software. Exact unique
   serial/BIOS matches can link to an asset; ambiguous devices require an audited
   administrator decision.
6. **Administration and contract** — `agents.read`/`agents.manage` permissions,
   server-side fleet search/freshness/summary, enrolment, link/unlink,
   dismiss/restore, atomic asset creation, revocation, asset device-inventory
   projection, generated OpenAPI and positive/negative integration tests are present.
7. **Enterprise packaging** — a major-upgrade WiX MSI supports silent per-machine
   install, GPO assignment and SCCM detection/supersedence. CI builds an explicitly
   unsigned short-retention validation artifact; production builds require verified
   Authenticode signing. An optional manager-only API recalculates the artifact hash,
   enforces production Authenticode trust, and streams the MSI without exposing an
   enrolment token.

Milestone 6 is not an estate rollout sign-off. Exit to broad deployment still requires
signed packages, representative Windows 10/11 TPM install/reboot/upgrade/uninstall
testing, the real TLS/proxy path, GPO and SCCM pilot rings, sampled asset-match review,
rollout-profile revocation and recorded rollback evidence. See
[`endpoint-agent.md`](../features/endpoint-agent.md) and
[`agent-deployment.md`](../operations/agent-deployment.md).

## Milestone 6.1: agent lifecycle and rollout hardening

Milestone 6.1 closes the defects found by the post-Milestone-6 backend and Windows
agent review. It is complete only when:

1. **Reproducible delivery** — the rewrite source, migrations, tests, MSI and generated
   contract are committed together; the legacy agent artifact pipeline is retired;
   CI uploads the MSI with its matching manifest and reruns every repository gate.
2. **Ordered offline delivery** — queued snapshots remain strict FIFO, retry wake-up
   honours the earliest backoff, newer sequences cannot overtake an older failure,
   `Retry-After` is respected, and server sequence/cadence responses are validated.
3. **Safe fleet scheduling** — enrollment and check-in retries use bounded exponential
   backoff plus stable per-device jitter so a GPO/SCCM rollout behind one proxy does
   not create a synchronized request burst.
4. **Supported identity replacement** — an audited, concurrency-safe administrator
   workflow replaces a revoked device identity after TPM/key loss or reimage without
   database editing, while active identities cannot be silently taken over.
5. **Complete link lifecycle** — revocation cannot strand an asset on an unusable
   agent record, replacement is audited and atomic, and retired/disposed assets cannot
   be selected by automatic or manual linking.
6. **Bounded SQLite telemetry** — per-agent ingress controls permit legitimate offline
   queue draining but reject abusive bursts; unchanged full snapshots are not stored
   repeatedly; detailed snapshot retention is bounded and database-volume capacity is
   monitored.
7. **Repair-safe enterprise MSI** — maintenance and major upgrades preserve the server
   URL, staged-token path and software-key policy, and the rewrite package explicitly
   blocks or upgrades the legacy product instead of allowing two services to own the
   same executable path.
8. **Fail-closed backend security** — Production requires Administrator MFA, request
   logs redact QR path tokens, password changes/resets have dedicated limits, and
   negative integration tests cover each boundary.
9. **Correct school operations** — warranty business dates use the persisted school
   timezone, readiness observes both database and backup volumes, and restore evidence
   covers the new agent lifecycle stores.

Repository implementation status: **complete on 2026-07-26**. The automated release
evidence is recorded in
[`release-verification-2026-07-26.md`](../evidence/milestone-6.1/release-verification-2026-07-26.md).

The external estate gates from Milestone 6 still apply after 6.1: real organisational
signing, physical Windows/TPM lifecycle testing, the deployed TLS/proxy path,
production-size backup/restore, and controlled GPO/SCCM pilot rings remain deployment
evidence rather than repository-only claims.

## Milestone 7: operational inventory workflows

Milestone 7 turns the completed register API into a daily operational tool. It is
complete only when:

1. **Dashboard and exception queues** — permitted users can retrieve bounded,
   drill-down-ready counts by status, condition, category, custody and location,
   including category/location stock such as the number of keyboards held in each
   room. Missing-location, missing-serial, inactive-holder, overdue-loan and other
   actionable exceptions are queryable without downloading the full register.
2. **Physical stocktakes** — a stocktake snapshots its expected scope at start,
   accepts idempotent asset-tag observations, records observed location/condition,
   reports missing, unexpected, wrong-location and condition-change discrepancies,
   and closes or cancels without silently altering curated asset data.
3. **Managed asset tags** — a versioned school tag policy controls prefix, number
   width and next sequence. Explicit server generation is atomic under concurrency
   while existing supplied tags and deterministic import reconciliation remain
   supported.
4. **Controlled bulk changes** — bounded asset selections use validate/preview and
   explicit commit with versions, expiry, fingerprint, all-or-nothing persistence,
   audit and asset history. Bulk operations cannot bypass assignment or terminal
   lifecycle workflows.
5. **Leaver recovery** — staff can open a leaver case for a person, see every asset
   still in their custody, and complete the case only after all custody is closed;
   creation never silently returns or reassigns equipment.
6. **Temporary loans** — assignments may have an expected return date, overdue state
   uses the persisted school timezone, due dates can be changed with concurrency
   protection, and overdue loans have a bounded query.
7. **Asset attachments** — bounded PDF/JPEG/PNG evidence for invoices, warranties,
   disposal certificates and photos is validated by content signature, hashed,
   archived rather than deleted, permission-gated and recovered with SQLite backup.
8. **Saved asset searches** — each user can maintain a bounded private set of
   strongly validated asset filters with at most one personal default; users cannot
   read or modify another user's searches.
9. **Asset bundles** — individually tagged assets can be grouped as a versioned
   laptop/charger/dock/case bundle without losing independent lifecycle, custody or
   audit records, and one asset cannot belong to multiple active bundles.

Repository implementation status: **complete on 2026-07-27**. The automated release
evidence is recorded in
[`release-verification-2026-07-27.md`](../evidence/milestone-7/release-verification-2026-07-27.md).

Production deployment still requires the documented TLS/proxy validation,
production-size backup/restore rehearsal, attachment-scanner monitoring, signed agent
artifacts, and controlled GPO/SCCM pilot evidence.
