# Docker hosting with PostgreSQL

`compose.postgres.yml` is the supported Linux-container topology. It runs:

- one non-root, read-only ASP.NET Core API container;
- PostgreSQL 18 on an internal-only network and durable named volume;
- the official ClamAV 1.4 daemon on the internal data network and a separate
  outbound-capable signature-update network;
- separate PostgreSQL bootstrap, schema-owner, and least-privilege runtime roles;
- an explicit one-shot EF migration service;
- an optional `pg_dump` backup job.

PostgreSQL and ClamAV are not published to the host. The API binds to
`127.0.0.1:8080` by default and must sit behind the school's HTTPS reverse proxy.

## Prepare configuration and secrets

Copy `.env.example` to `.env` and set the real public host, browser origin, and QR URL.
Create `rewrite/secrets` and supply:

| File | Content |
| --- | --- |
| `postgres-admin-password.txt` | Bootstrap superuser password, used only by PostgreSQL and the role-initialisation job |
| `postgres-owner-password.txt` | Migration/schema-owner password, also used by the backup job |
| `postgres-app-password.txt` | Least-privilege API runtime password |
| `data-protection.pfx` | A PFX with a private key, used only to encrypt the shared ASP.NET Core key ring |
| `data-protection-password.txt` | The PFX export password |

Use a different high-entropy value for every PostgreSQL role. The directory is
gitignored. Restrict it to the deployment account and keep a protected copy of the
PFX/password with the backup recovery material. Losing both the PFX and persisted key
ring invalidates cookies and can make protected MFA/QR material unrecoverable.

Budget at least 4 GB of RAM for ClamAV in addition to the API and PostgreSQL allocation;
the signature database is intentionally persisted in a named volume.
Only ClamAV joins the outbound-capable `signature_updates` network. Permit FreshClam
to reach the official update service or configure an approved internal signature
mirror/proxy; restrict this egress with host/network policy as appropriate. Port 3310
is not published. API readiness now requires ClamAV `PONG` and a signature database
published within the configured age limit (72 hours by default). Test signature
updates and `/health/ready` on the actual Linux host before production sign-off.

To enable the optional Linux agent-installer download, additionally use
`compose.agent-installer.yml` and its independently provisioned release certificate,
approved version, and digest; see [agent deployment](agent-deployment.md).

Review the resolved configuration without printing secret contents:

```powershell
docker compose --env-file .env -f .\compose.postgres.yml config
```

## Build and start

```powershell
docker compose --env-file .env -f .\compose.postgres.yml build
docker compose --env-file .env -f .\compose.postgres.yml up -d
docker compose --env-file .env -f .\compose.postgres.yml ps
```

The `migrate` service must exit `0`; the API waits for that result, PostgreSQL
readiness, and ClamAV readiness. Check:

```powershell
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
docker compose --env-file .env -f .\compose.postgres.yml logs migrate api
```

Only the trusted `172.30.0.0/24` proxy-facing Compose network may supply forwarded
headers. Keep the published port loopback-only and proxy it through HTTPS. If the
network ranges are changed, update both the Compose IPAM block and
`ForwardedHeaders__KnownNetworks__0`.

## Apply later migrations

Pull/build the new image, take a verified backup, stop normal writes, then run:

```powershell
docker compose --env-file .env -f .\compose.postgres.yml run --rm migrate
docker compose --env-file .env -f .\compose.postgres.yml up -d api
```

The API refuses to serve while migrations are pending.

## Backup and restore rehearsal

Create a custom-format dump and checksum manifest in the durable `backups` volume:

```powershell
docker compose --env-file .env -f .\compose.postgres.yml `
  --profile operations run --rm postgres-backup
```

Copy the completed `backup-postgres-*` directory to encrypted backup storage using the
host's backup tooling. Back up the `data_protection_keys` volume and PFX recovery
material in the same recovery set, but with separate access control.

Rehearse restore into a throwaway database, never over the live database:

```powershell
docker compose --env-file .env -f .\compose.postgres.yml exec postgres `
  sh -ec '
    export PGPASSWORD="$(tr -d "\r\n" < /run/secrets/postgres_admin_password)"
    createdb -h 127.0.0.1 -U postgres -O inventory_owner inventory_restore_test
  '

# Copy the selected dump to an operator-controlled temporary path in the DB container,
# then:
docker compose --env-file .env -f .\compose.postgres.yml exec postgres `
  sh -ec '
    export PGPASSWORD="$(tr -d "\r\n" < /run/secrets/postgres_admin_password)"
    pg_restore -h 127.0.0.1 -U postgres --role=inventory_owner --no-owner --no-privileges -d inventory_restore_test --clean --if-exists /path/to/inventory.dump
  '
```

Verify migrations, row counts, administrator login/MFA, audit history, attachment
hashes, asset history, and QR unprotection against the isolated restore. Drop only the
explicit `inventory_restore_test` database after evidence is captured. The API
`--backup` and `--rehearse-restore` commands remain SQLite-only and fail clearly when
PostgreSQL is selected.

## Existing SQLite data

Provider selection is not a migration tool. The current CSV exports/imports are
register/reference-data interchange only. They do **not** transfer identities and
MFA secrets, agent keys/trust, history/audit, attachments, relationships, or the
data-protection key ring. Do not describe CSV transfer as a lossless cutover or
switch an existing installation to PostgreSQL on that basis. PostgreSQL can be
used for a new installation; migrating an existing SQLite installation needs a
separate full-state migration and rehearsed rollback plan.
