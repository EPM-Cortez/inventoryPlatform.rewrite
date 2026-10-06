# Monitoring

## Probes

- `GET /health/live` proves the process can serve HTTP.
- `GET /health/ready` proves the database is reachable and fully migrated, the
  configured attachment malware scanner is usable, and the SQLite database volume
  and configured backup storage pass capacity/age checks.

Use liveness to restart a dead process and readiness to decide whether it should
receive traffic. A database outage should not create a restart loop.

## Logs and alerts

Every HTTP response includes `X-Correlation-ID`. The structured completion log records
method, path, status, duration, and that identifier; audit records created within the
request carry the same correlation value. Collect application and reverse-proxy logs
centrally. Alert on:

- sustained readiness failures;
- HTTP `500` and `503` rates;
- repeated `401`, `403`, `429`, and failed-login audit events;
- optimistic-concurrency conflicts above the normal editing baseline;
- administrator, role, password-reset, disable-user, and retirement audit events;
- database lock/busy spikes, storage capacity, WAL growth, missing/stale backups, and
  backup/rehearsal failures;
- certificate expiry and data-protection key-path write failures.

Do not log passwords, cookies, antiforgery tokens, connection strings, request
authorization headers, full exported personal data, or QR token path segments.
Application completion logs replace every `/api/v1/qr/<value>` suffix with
`/api/v1/qr/{token}`.
Configure reverse-proxy access logs to replace `/api/v1/qr/<value>` with
`/api/v1/qr/{token}`. Correlate incidents with request identifiers and audit event
identifiers instead.

`GET /api/v1/operations/status` requires `settings.manage` and returns process-start
time, counters, separate backup- and SQLite-volume capacity state, and threshold alerts. Counters cover failed
login, failed MFA, rejected imports, QR failures, SQLite busy/locked events, and
readiness failures. They are intentionally aggregate and never label a QR token,
person, email, asset tag, or request body. Counters reset with the process; forward
them to the chosen production metrics sink if cross-restart history is required.

The QR resolver, readiness probe, and anonymous authentication routes use per-IP
limits. Authenticated password operations, attachment uploads, bulk previews, and
stocktake starts partition by user ID, so staff behind the same school proxy or NAT
do not consume one another's allowance. Forwarded client addresses are accepted only
when `ForwardedHeaders:Enabled=true` and the immediate proxy/network is allow-listed.
Test alerts by setting a low threshold in a non-production environment, causing the
corresponding safe failure, and confirming both the status alert and correlated log.
