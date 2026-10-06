# ADR 0005: PostgreSQL container provider

## Status

Accepted; supersedes ADR 0004 where it says PostgreSQL is not implemented.

## Decision

Retain SQLite as the default and supported single-host provider. Add PostgreSQL as an
explicit provider selected by `Database:Provider=Postgres` for the Docker deployment.
The providers share the domain and EF model but have separate migration assemblies:

- `Inventory.Infrastructure` owns SQLite migrations;
- `Inventory.Infrastructure.PostgresMigrations` owns PostgreSQL migrations.

The serving process never applies migrations. Operators or the Compose one-shot
`migrate` service invoke `--migrate` before API startup. Provider selection does not
perform data conversion or copy an existing SQLite database.

PostgreSQL migrations reproduce the cross-row SQLite safeguards with PL/pgSQL triggers.
Advisory transaction locks serialize quota and cross-table races that SQLite previously
serialized through its single-writer model. CI runs representative asset, custody,
stocktake, import/export, attachment, saved-search, bundle, and signed-agent workflows
against PostgreSQL.

The Linux API container uses the official ClamAV daemon image for fail-closed
attachment scanning. Database and data-protection certificate passwords are mounted as
files rather than committed to Compose or passed in the database connection string.

## Consequences

- Every model change requires review and drift checks for both migration sets.
- PostgreSQL uses `pg_dump`/`pg_restore`; the in-process SQLite backup/rehearsal
  commands deliberately reject PostgreSQL.
- Moving existing production data between providers requires a separately rehearsed
  export/import, count reconciliation, signed exception report, and rollback plan.
- PostgreSQL permits future horizontal API scaling, but process-local rate limits and
  metrics mean this deployment intentionally starts with one API replica.
