# Legacy reconciliation

Legacy reconciliation wraps the existing bounded asset importer without weakening it.
All endpoints require the Administrator-only `legacy.reconcile` permission.

## Deterministic dry run

`POST /api/v1/legacy-reconciliation/dry-runs` accepts the same multipart CSV and
explicit `CreateOnly` or `UpsertByAssetTag` mode as asset validation. One SQLite
transaction persists:

- the ordinary immutable import job and validation audit;
- source, valid/error, create/update, target-before, and expected-target-after counts;
- duplicate, unmapped-reference, data-quality, and mapping-decision issues;
- generated-tag, planned category/location, partial-date, fuzzy-person, and
  import-unassigned summaries;
- a bounded deterministic source-to-target sample; and
- an append-only reconciliation report with a SHA-256 fingerprint.

The fingerprint excludes job IDs, actor, and timestamps. Repeating the same bytes,
mode, and target state therefore produces the same fingerprint and issue keys; an
integration test proves this. Target state changes intentionally change evidence.

Missing categories/locations remain explicit planned atomic creations. An unresolved
former person/department is reported and can remain import-unassigned under the
import-only rule. Duplicate and validation failures remain blocking.

## Sign-off and exceptions

`POST /api/v1/legacy-reconciliation/{importJobId}/sign-off` records one immutable
authenticated `Approved` or `Rejected` decision. Approval of a report containing
blocking issues requires the caller to submit every stable issue key with a bounded
rationale. Those signed exceptions document exclusions or cleanup decisions only:

- they never change the import job;
- they never turn `ValidationFailed` into `ReadyToCommit`;
- they never bypass snapshot hashing, optimistic versioning, reference rechecks, or
  atomic commit; and
- a second sign-off is rejected—create a new dry run for a new decision.

The sign-off and a redacted audit summary are committed together. Full rationales live
only in permission-protected reconciliation evidence, not in audit details.

## Cutover package

`GET /api/v1/legacy-reconciliation/{importJobId}/cutover-package` returns a
`no-store` JSON attachment containing source/report/package hashes, current import
version/status, signed counts and decision, readiness blockers, and explicit cutover
and rollback procedures.

`cutoverReady` is true only for an approved, zero-error, still-current
`ReadyToCommit` job (or its subsequently committed evidence). A package with signed
blocking exceptions remains not ready. Operators must create and verify a fresh
database/key-ring backup before commit, compare committed reconciliation counts, run
the recorded samples, and retain the package with the release evidence.
