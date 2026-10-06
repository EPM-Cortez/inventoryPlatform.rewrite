# Sample data

The rewrite does not seed realistic people, assets, credentials, or production-like
school data automatically. This prevents demonstrations from becoming hidden
production state.

For development:

- create the first administrator with `scripts/seed-admin.ps1`; no default credential
  is provided;
- create categories and locations through their APIs;
- create assets through `POST /api/v1/assets`;
- use clearly synthetic names, tags, serials, and purchase details;
- keep deterministic builders inside test projects for automated scenarios.

Never copy live personal data, passwords, exports, or database backups into source
control. A future opt-in sample-data command must be idempotent, Development-only,
clearly label its records, and have tests proving it cannot run in Production.
