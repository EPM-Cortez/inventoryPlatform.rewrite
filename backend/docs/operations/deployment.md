# Deployment

For a fresh Linux/PostgreSQL release, complete the
[production release gate](production-readiness.md) before go-live.

1. Build and test the exact source revision.
2. Check both provider models for drift and review the selected provider's migration
   range.
3. Stop writes and back up the selected database plus the data-protection key ring.
4. Publish the API with `dotnet publish`.
5. Supply connection string, durable key path, non-loopback HTTPS QR base URL, host
   allow-list, warranty threshold, and exact CORS origins from deployment
   secret/configuration systems.
6. Apply the database migration as a separate one-shot step.
7. Run the idempotent `--seed-roles` command as a preflight; normal serving startup
   repeats this reconciliation before accepting requests. On a clean installation,
   run the secret-backed `--seed-admin` command once.
8. Start Kestrel behind an HTTPS reverse proxy or managed ASP.NET Core host.
9. Verify `/health/live`, `/health/ready`, login/CSRF, and one read-only asset request.
10. Keep the previous application artifact and database backup for rollback.

For SQLite, run one write-capable instance and keep the database, `-wal`, and `-shm`
files on local durable storage. SQLite cannot generate idempotent migration scripts; use
`dotnet ef database update` or a script from the known currently applied migration.
Normal serving startup fails while migrations are pending and never applies them
implicitly. Role-preset drift is repaired transactionally after the migration check;
the repair is audited and existing cookies receive the current claims on their next
request.

Configure `ForwardedHeaders:KnownProxies` (or `ForwardedHeaders:KnownNetworks`) with
the actual proxy addresses so client IPs used for rate limiting and audit records are
correct behind the proxy. Headers from unlisted sources are ignored; never add
arbitrary networks to the trusted list.

For the PostgreSQL container topology, follow
[`docker-postgres.md`](docker-postgres.md). PostgreSQL and ClamAV stay on an internal
network, the API remains loopback-published behind HTTPS, secrets are file-mounted,
and the one-shot migrator must complete before serving.
