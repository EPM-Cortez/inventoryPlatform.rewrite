# History and audit

`AssetEvent` explains an asset lifecycle to inventory users. `AuditLog` records
security/administrative behavior for administrators and auditors.

Both are append-only in the DbContext. Normal APIs expose no update/delete path.
Asset commands add their event before one unit-of-work save. Login, logout, user
creation, role changes, disable, password change/reset, and bootstrap create audit
records. QR label printing and asset import validation, successful/rejected commit,
and successful/rejected export also create audit rows with bounded structured
details. Import-created and import-updated asset events include the persisted job ID
and source row so reconciliation can trace a changed asset back to its snapshot.

`GET /api/v1/audit` exposes the audit log to holders of the `audit.read` permission
(Administrator and ReadOnlyAuditor presets). Results are paged newest-first and can
be filtered by action, target type/id, actor, outcome, and occurred-at range.

The public vocabulary is canonical: actions are lower-case dot-separated events
with snake-case resource names and past-tense verbs, target types are lower-case
snake case, and successful outcomes are `succeeded`. Historic rows remain immutable.
The query/export boundary maps legacy spellings such as `success`,
`saved-search.create`, `stocktake.asset-observed`, and `assetImportJob` to their
canonical forms. A canonical filter matches both historic aliases and new records,
so `outcome=succeeded` does not hide old `success` rows.

`GET /api/v1/audit/vocabulary` returns the distinct canonical actions, target
types, and outcomes currently present, sorted and capped at 200 values per field.
The response sets `truncated` if any field reached the cap. Frontends can build
filter choices from this endpoint instead of maintaining a parallel vocabulary.

`GET /api/v1/audit/export` requires the separate `audit.export` permission and applies
the same validated filters. It is capped by `Audit:MaxExportRows`, reports truncation
and row count in response headers, neutralises spreadsheet formulas, and deliberately
omits `DetailsJson` so password/MFA/reset or other structured internals cannot be
bulk-exposed.

MFA enrolment/reset, school settings, operational exports, legacy reconciliation dry
runs, immutable reconciliation sign-offs, and real code-managed role-preset repairs
are audited. A role claim repair and its `identity.role_presets.reconciled` row share
one SQLite transaction. Request audit records carry the same correlation identifier
returned as `X-Correlation-ID`. Retention remains an operator policy because deletion
is intentionally not exposed through the API.
