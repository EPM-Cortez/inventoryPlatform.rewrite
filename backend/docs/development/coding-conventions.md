# Coding conventions

- Target `net10.0`, C# 14, nullable reference types, deterministic builds, and warnings
  as errors.
- Keep domain rules in `Inventory.Domain` and use-case orchestration in
  `Inventory.Application`.
- Controllers translate HTTP contracts only; they do not query EF Core or own business
  rules.
- Infrastructure implements application ports and owns EF Core, Identity, SQLite,
  and external integrations.
- Use UTC `DateTimeOffset`, `Guid` identifiers, explicit cancellation tokens, and
  `ProblemDetails` errors.
- Normalise asset tags and serial numbers before uniqueness checks.
- Never hard-delete asset history or audit rows.
- Avoid new dependencies unless the platform cannot reasonably provide the capability;
  pin and audit every accepted package centrally.
- Do not hand-edit EF migration designer files or the generated OpenAPI snapshot.
- A deliberately empty migration may contain reviewed provider SQL such as the WAL
  pragma; document why it is hand-authored.

Formatting is enforced by `dotnet format --verify-no-changes`. Name tests for observable
behaviour, and keep permission names in the central `Permissions` catalogue.
