# Incident runbook

## First response

1. Preserve logs and note the UTC start time, release, host, and affected endpoints.
2. Check `/health/live`, `/health/ready`, reverse-proxy status, SQLite file/WAL
   permissions, local disk capacity, and recent deployment or migration activity.
3. Limit impact with the narrowest reversible action. Do not delete audit/history
   data or weaken authentication.
4. Capture a timeline and assign an incident owner.

## Compromised or disabled account

- An administrator can disable the account through the user API. The next authenticated
  request is rejected because enabled state is checked against the database.
- Reset its password through the supported endpoint; this is atomic, sets
  `MustChangePassword`, rotates the security stamp, and invalidates sessions.
- Preserve the relevant audit events and investigate role changes and high-risk actions.
- Protect the final enabled administrator account; create a separate recovery
  administrator through the documented bootstrap path when no administrator exists.

## Failed deployment or migration

- Stop routing new traffic to the failed release.
- If no irreversible schema change was applied, redeploy the last known-good release.
- For an applied migration, follow the reviewed forward-fix or rollback notes; never
  edit migration history manually.
- Re-run readiness, authentication, asset lookup, and history checks before restoring
  traffic.

## Suspected data loss

- Put writes into an operator-controlled maintenance window.
- Take a forensic backup before repair.
- Reconcile assets, events, and audit records; do not fabricate missing history.
- Restore using `backup-and-restore.md` if reconciliation cannot safely repair the data.

## Custody inconsistency

- Stop assignment writes while investigating; ordinary reads can remain available if
  they are internally consistent.
- Compare asset status/current-assignment API results with open `AssetAssignments`
  rows. Never edit or delete assignment history ad hoc.
- Preserve a backup, request/trace identifiers, assignment and asset versions, and the
  related AssetEvents.
- A stale return or transfer is safe to retry only after the operator reloads both the
  current asset and assignment versions.
- If a failed deployment leaves model/schema mismatch, restore the compatible release
  or apply a reviewed forward migration. Do not bypass
  `UX_AssetAssignments_OpenAsset`.

## Damaged or compromised QR label

- A manager or administrator calls `POST /api/v1/assets/{assetId}/qr/rotate`.
- Confirm the response has the next token version, the old payload returns the same
  non-sensitive `404` as an unknown token, and `QrRotated` exists in asset history.
- Reprint from the new QR image endpoint. Do not paste either payload token into the
  incident log or ticket.
- If data-protection keys were lost, existing token resolution still uses its database
  hash, but image regeneration fails. Rotate only the labels that require reprinting
  and restore key-ring backup procedures before continuing.

## Import or export incident

- Validation never changes assets. Download `errors.csv`, correct the source, and
  create a new validation job rather than altering the stored snapshot.
- A `409 import-snapshot-stale` or identifier conflict means a referenced record
  changed after validation. Reload live data and validate again; do not bypass the
  captured versions.
- After a timeout or lost client connection during commit, retrieve the job. A
  `committed` job must not be replayed; use its reconciliation counts and the
  `ImportCreated`/`ImportUpdated` events containing the job ID. A `readyToCommit` job
  has not had its commit transaction persisted.
- SQLite and EF apply the bounded commit in one transaction. If database integrity or
  disk failure makes the result uncertain, stop writes and take a forensic backup
  before reconciliation.
- There is no destructive automatic import rollback. Correct an erroneous committed
  import with reviewed forward changes or restore the verified pre-import backup in a
  maintenance window; retain events, audits, and the import job.
- Export is read-only. Preserve its `assets.exported` audit row, normalized filters,
  and row count when investigating unexpected disclosure, and rotate affected account
  sessions rather than deleting audit evidence.
