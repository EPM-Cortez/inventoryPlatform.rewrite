# ADR 0004: SQLite production provider

- Status: Accepted
- Date: 2026-07-24

## Context

The school cannot operate a full SQL Server. The inventory workload is a single-school,
operator-focused service whose expected write concurrency fits a local embedded
database. Tests should exercise the same provider used in production.

## Decision

Use the Microsoft-maintained EF Core SQLite provider for development, tests, and
production. Production uses an absolute database path on local durable storage, one
write-capable API instance, foreign keys, a 30-second busy timeout, connection pooling,
and persistent WAL mode.

SQLite does not provide application-level encryption here. Protect its directory and
backups with filesystem ACLs and operating-system or volume encryption. Never place
the live database on SMB, NFS, or another network filesystem.

Integration tests apply the real migrations to both in-memory and temporary file-backed
SQLite databases. Application-managed GUID concurrency tokens remain provider-neutral.
UTC timestamps are converted to `DateTime`; money is stored as integer minor units.

## PostgreSQL exit path

If write concurrency, multiple API replicas, hosting topology, or database size makes
SQLite unsuitable, move to PostgreSQL as a deliberate deployment migration. Keep
Domain and Application unchanged, add the Npgsql provider in Infrastructure, generate
and review PostgreSQL-specific migrations, reconcile and verify copied data, then cut
over. Do not support SQLite and PostgreSQL through an untested runtime toggle.

## Consequences

Operations are lightweight and require no database server. SQLite schemas and
idempotent migration scripts are unavailable, only one writer can commit at a time,
and backup/restore procedures must account for WAL state.

## References

- [EF Core SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [SQLite write-ahead logging](https://www.sqlite.org/wal.html)
- [Microsoft.Data.Sqlite connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)
