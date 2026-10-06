# Acceptance criteria status

Status reflects the code in this repository, not the final brief target.

| ID | Status | Evidence / remaining work |
| --- | --- | --- |
| AC-01 | Implemented | Create, ID/tag lookup, operational register fields, expanded text search, and paging |
| AC-02 | Implemented | Duplicate normalized tag returns documented `409` |
| AC-03 | Implemented | Guid concurrency token and stale-write integration test |
| AC-04 | Implemented | Person/department assign, transfer, return, and preserved custody periods |
| AC-05 | Implemented | Unavailable states reject assignment; terminal states require retire/dispose actions; only custody actions set Assigned/InStock |
| AC-06 | Implemented | SVG/PNG generation, opaque payload, authenticated exact-asset resolution, and format/privacy integration tests |
| AC-07 | Implemented | Atomic rotation revokes the prior token; old/new resolution and history are tested |
| AC-08 | Implemented | All five date-boundary states, summaries, filters, and expiry report are tested |
| AC-09 | Implemented | Create/update, custody, warranty, QR, retire/restore/dispose, notes, and changed import rows create immutable events |
| AC-10 | Implemented | Persisted row/column validation, stable generation for blank asset/reference keys, canonical schemas, sampled STR/reference inputs, and error CSV are tested to leave live data unchanged |
| AC-11 | Implemented | Manager/admin permission, explicit versioned asset/reference commits, reconciliation counts, events, preflight conflicts, and an injected late SQLite rollback with rejection audit are tested |
| AC-12 | Implemented | Asset and permission-separated reference exports use stable UTF-8, RFC escaping, formula neutralisation, round trips, limits, and actor/row-count audit |
| AC-13 | Implemented | Business APIs use named authorization decisions; an architecture test rejects endpoints without an explicit authorize/anonymous decision, and role-negative boundaries are tested |
| AC-14 | Implemented | Secure initial seed plus versioned create/edit, role set, disable, archive/restore, and reset without DB editing |
| AC-15 | Implemented | Runtime and checked-in OpenAPI 3.1 include permissions, structured examples, Problem Details errors, and automated drift enforcement |
| AC-16 | Implemented | Pinned build/test/migration docs, online backup command, hashed manifest, isolated timed restore rehearsal, operator report, and rollback procedure |
| AC-17 | Implemented | Persisted deterministic legacy dry runs include duplicate/unmapped reports, counts, sampled comparisons, authenticated signed exceptions, repeatability proof, and cutover/rollback packages |
| AC-18 | Implemented | Architecture test rejects `TenantId`/`SchoolId` on domain rows |
| AC-19 | Implemented | No frontend project or UI implementation |
| AC-20 | Implemented | Individual and bounded batch PDFs use exact stock dimensions, support partial sheets, enforce `qr.print`, and are audited |
| AC-21 | Implemented | Agent check-ins preserve strict FIFO ordering, bounded retry/jitter, validated sequence/cadence, per-agent burst and daily quotas, compact unchanged receipts, and bounded detailed retention |
| AC-22 | Implemented | Administrator-only one-time agent key recovery is hashed, expiring, audited, concurrency-safe, immediately revokes the old identity, and requires explicit asset relinking; terminal asset links are blocked in service and SQLite |
| AC-23 | Implemented | Production fails closed for Administrator MFA, QR tokens are redacted from request logs, password operations have separate per-IP limits, school-timezone warranty boundaries are tested, both storage volumes affect readiness, and restore manifests cover agent lifecycle stores |
| AC-24 | Implemented | Dashboard totals and exception queues are bounded, permission-gated and drill down to existing asset filters, including category counts by storage location |
| AC-25 | Implemented | Stocktakes preserve an immutable expected snapshot, accept idempotent observations, calculate discrepancies, and never silently mutate asset data |
| AC-26 | Implemented | Managed asset-tag allocation is versioned, atomic and unique under concurrent manual and agent-driven creation |
| AC-27 | Implemented | Bulk changes require a validated, expiring preview and versioned all-or-nothing commit; unsafe custody and terminal transitions are rejected |
| AC-28 | Implemented | Leaver cases list current custody and cannot complete until every open assignment is closed |
| AC-29 | Implemented | Optional expected-return dates and school-timezone overdue queries are concurrency-safe and preserved in assignment history |
| AC-30 | Implemented | PDF/JPEG/PNG attachments are magic-byte validated, malware-scanned, bounded, hashed, permission-gated, archived, backed up and downloaded only as attachments |
| AC-31 | Implemented | Saved searches are private, bounded, strongly validated and enforce zero or one default per user |
| AC-32 | Implemented | Bundles group independently tagged assets with versioned membership and prevent one asset from joining multiple active bundles |
