# Documentation map

| Area | Documents | Owner |
| --- | --- | --- |
| Source brief | `product/brief-pack.md` | Product owner |
| Scope and delivery | `product/scope.md`, `product/milestone-plan.md`, `product/acceptance-criteria.md` | Product + engineering |
| Architecture | `architecture/overview.md`, `architecture/data-model.md`, `architecture/adr/` | Engineering |
| API | `api/conventions.md`, `api/examples.md`, `api/permissions.md`, `api/openapi-v1.json` | API maintainers |
| Features | `features/` | Feature owners |
| Operations | `operations/` | Service operators |
| Development | `development/` | Engineering |
| Release evidence | `evidence/` | Engineering + service operators |

`README.md` is the runnable entry point. `AGENTS.md` is the authoritative condensed
guidance for coding assistants.

Milestones 0 through 7 are implemented. Milestone 6.1 release evidence is recorded
in `evidence/milestone-6.1/release-verification-2026-07-26.md`; Milestone 7 evidence
is recorded in `evidence/milestone-7/release-verification-2026-07-27.md`.
Deployment-specific rehearsals and endpoint estate pilots are explicitly labelled so
repository evidence is not confused with a production-host sign-off.

ADR 0004 records the original SQLite decision. ADR 0005 supersedes its provider
boundary by adding a separately migrated and tested PostgreSQL Docker deployment while
retaining SQLite support.

Current feature guides include assets, assignments, people/departments, QR codes,
QR label printing, warranties, history/audit, controlled asset and reference-data
import/export, user administration, the secure Windows endpoint agent, and
`features/operational-workflows.md` for dashboards, stocktakes, managed tags,
controlled bulk changes, leavers/overdue loans, attachments, saved searches, and
asset bundles.
Milestone-5 guides additionally cover MFA, school settings, audit export,
observability, online backup/restore rehearsal, and legacy reconciliation/cutover
evidence.

Endpoint deployment and production signing/GPO/SCCM pilot gates are documented in
`operations/agent-deployment.md`.
