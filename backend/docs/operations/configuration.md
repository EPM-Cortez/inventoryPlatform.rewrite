# Configuration

## Required

| Key | Purpose |
| --- | --- |
| `Database:Provider` | `Sqlite` (default) or `Postgres` |
| `ConnectionStrings:InventoryDatabase` | Provider connection; SQLite requires an absolute local path in Production |
| `Database:PasswordFile` | Optional absolute PostgreSQL password secret file; overrides a connection-string password |
| `Security:DataProtectionKeysPath` | Persistent key ring; mandatory in Production |
| `Storage:BackupPath` | Separate persistent local backup root; mandatory in Production |
| `School:QrBaseUrl` | Absolute HTTPS client scan URL used as the QR payload prefix |

## Optional

| Key | Purpose |
| --- | --- |
| `Cors:AllowedOrigins` | Exact browser origins when cross-origin cookies are required |
| `BootstrapAdmin:*` | One-shot `--seed-admin` command only; supply through secret configuration |
| `Logging:LogLevel:*` | Structured logging levels |
| `AllowedHosts` | Host-header allow-list |
| `School:Name`, `School:Code`, `School:TimeZone` | Defaults used before the one-row school profile is persisted |
| `School:WarrantyWarningDays` | Expiring-soon threshold, 1-365; defaults to 60 |
| `Mfa:EnforceRequiredRoles` | Require configured roles to enrol MFA before using business permissions; must remain true in Production |
| `Mfa:RequiredRoles` | Roles subject to forced enrolment; Production requires Administrator to remain present |
| `Mfa:RecoveryCodeCount` | One-time recovery codes issued per set; defaults to 10 |
| `Mfa:Issuer` | Authenticator application issuer label |
| `Identity:ReconcileRolePresetsOnStartup` | Reconcile code-managed role claims before serving; defaults to true and cannot be disabled in Production |
| `ImportExport:MaxImportBytes` | Actual CSV byte limit; defaults to 5,242,880 |
| `ImportExport:MaxImportRows` | Atomic import row limit; defaults to 500 |
| `ImportExport:MaxExportRows` | Maximum rows in one asset or reference-data export; defaults to 10,000 |
| `ImportExport:PreviewRows` | Valid rows included in the job preview; defaults to 25 |
| `ImportExport:PreviewErrors` | Errors included inline before using `errors.csv`; defaults to 100 |
| `ImportExport:LegacyDefaultCurrency` | Currency applied to cost values in the supported STR legacy register; defaults to GBP |
| `ImportExport:GeneratedAssetTagPrefix` | Prefix for tags generated from an import file fingerprint and source row; shipped value is STR-AUTO |
| `ImportPreviews:RetentionDays` | Active validation payload lifetime, 1-30 days; defaults to 7. Expired uncommitted previews cannot be read or committed. |
| `ImportPreviews:MaximumActiveJobsPerUser` | Combined asset and reference-data validation jobs per user; defaults to 10. |
| `ImportPreviews:MaximumBytesPerUser` | Combined retained preview payload bytes per user; defaults to 67,108,864. |
| `ImportPreviews:MaximumBytesOverall` | Combined retained preview payload bytes across users; defaults to 536,870,912. |
| `LegacyReconciliation:SampleRows` | Deterministic source/target comparisons retained per dry run; defaults to 20 |
| `LegacyReconciliation:MaximumSignedExceptions` | Maximum blocking issues accepted in one sign-off; defaults to 1,000 |
| `Agents:CheckInIntervalSeconds` | Server-advertised check-in cadence, 300-86,400 seconds; defaults to 21,600 |
| `Agents:ConnectivityGraceSeconds` | Grace after the expected check-in before a device becomes stale, 60-86,400 seconds; defaults to 900 |
| `Agents:ConnectivityOfflineAfterSeconds` | Age of the last check-in before a device becomes offline; must exceed interval plus grace and be at most 2,592,000 seconds; defaults to 259,200 |
| `Agents:MaximumEnrollmentProfileDays` | Maximum administrator-created profile lifetime, 1-90 days; defaults to 30 |
| `Agents:MaximumOfflineSnapshotDays` | Oldest queued snapshot accepted after collection, 1-90 days; defaults to 30 |
| `Agents:MaximumKeyRecoveryMinutes` | Maximum signed recovery window for an enrolled agent key, 5-1,440 minutes; defaults to 60 |
| `Agents:CheckInBurstWindowSeconds` | Per-agent burst-accounting window, 60-3,600 seconds; defaults to 900 |
| `Agents:MaximumCheckInsPerAgentBurst` | Per-agent check-ins accepted during the burst window, 129-1,000; defaults to 144 |
| `Agents:CheckInQuotaWindowHours` | Longer per-agent quota window, 1-24 hours; defaults to 24 |
| `Agents:MaximumCheckInsPerAgentQuotaWindow` | Per-agent check-ins accepted in the quota window; defaults to 192 and must be at least the burst limit |
| `Agents:MaximumSnapshotRecordsPerAgent` | Retained append-only snapshots per agent before deterministic pruning; defaults to 64 |
| `Agents:MaximumSnapshotPayloadBytesPerAgent` | Retained canonical snapshot payload budget per agent; defaults to 16,777,216 bytes |
| `AgentInstaller:InstallerPath` | Optional absolute path to the MSI exposed to agent managers; configure with `ManifestPath` |
| `AgentInstaller:ManifestPath` | Optional absolute path to the matching build manifest JSON |
| `AgentInstaller:ManifestSignaturePath` | Detached RSA/SHA-256 signature over the exact manifest bytes; required for Linux Production delivery |
| `AgentInstaller:TrustedSigningCertificatePath` | Public release certificate PEM, provisioned independently of the artifact directory |
| `AgentInstaller:ApprovedVersion` | Explicit approved release version for the optional signed-manifest path |
| `AgentInstaller:ApprovedSha256` | Explicit approved MSI SHA-256 digest for the optional signed-manifest path |
| `AgentInstaller:MaximumArtifactBytes` | Validated installer size ceiling, 1 MiB-2 GiB; defaults to 1 GiB |
| `Audit:MaxExportRows` | Hard limit for one audit CSV export; defaults to 10,000 |
| `RateLimits:AuthenticationPermitLimit` | Login/MFA attempts per remote address per minute; defaults to 10 |
| `RateLimits:AgentEnrollmentPermitLimit` | Signed enrollment attempts per remote address per minute; defaults to 30 |
| `RateLimits:AgentCheckInPermitLimit` | Signed check-ins per remote address per minute; defaults to 600 |
| `RateLimits:QrResolutionPermitLimit` | QR resolutions per remote address per minute; defaults to 120 |
| `RateLimits:ReadinessPermitLimit` | `/health/ready` probes per remote address per minute; defaults to 30 |
| `RateLimits:PasswordOperationPermitLimit` | Independent self-service password-change and administrator password-reset attempts per remote address per minute; defaults to 10 |
| `RateLimits:MfaSelfServicePermitLimit` | Shared per-user budget for MFA enrollment, confirmation and recovery-code regeneration per minute; defaults to 5 |
| `RateLimits:ImportPreviewPermitLimit` | Shared per-user budget for asset, people, department and location import validation per minute; defaults to 5 |
| `ForwardedHeaders:KnownProxies` | Reverse-proxy IP addresses whose `X-Forwarded-For`/`X-Forwarded-Proto` headers are trusted |
| `ForwardedHeaders:KnownNetworks` | Trusted proxy networks in CIDR notation |
| `ForwardedHeaders:Enabled` | Explicitly enables trusted forwarding; defaults to false |
| `ForwardedHeaders:ForwardLimit` | Maximum trusted forwarding hops; defaults to 1 |
| `Security:ProtectKeysWithDpapi` | Encrypt persisted data-protection keys with Windows DPAPI; defaults to true on Windows |
| `Security:DataProtectionCertificatePath` | Absolute local PFX used to protect the key ring on non-Windows production hosts |
| `Security:DataProtectionCertificatePassword` | PFX password supplied only by the deployment secret store |
| `Security:DataProtectionCertificatePasswordFile` | Absolute secret file alternative to the inline PFX password |
| `AttachmentScanning:Provider` | `WindowsDefender` (default) or `ClamAv` |
| `AttachmentScanning:Host`, `AttachmentScanning:Port` | ClamAV daemon endpoint; defaults to localhost:3310 |
| `AttachmentScanning:MaximumSignatureAgeHours` | ClamAV readiness rejects missing or older virus signatures; defaults to 72 hours (1-168 allowed) |
| `Storage:MinimumFreeSpaceBytes` | Readiness/backup-volume capacity floor; defaults to 1 GiB |
| `Storage:MinimumDatabaseFreeSpaceBytes` | Readiness/SQLite-volume capacity floor; defaults to 1 GiB |
| `Storage:MaximumBackupAgeHours` | Stale-backup alert threshold; defaults to 24 hours |
| `Storage:RehearsalLoginEmail` | Enabled account verified in an isolated restore rehearsal |
| `Storage:RehearsalLoginPassword` | Rehearsal secret; prompt or secret-store input only |
| `Storage:RehearsalRequiresActiveQr` | Require an active QR/key-ring proof; defaults to true |
| `Observability:*AlertThreshold` | Positive process-local thresholds for failed auth/MFA, imports, QR, and SQLite contention |

Use double underscores for environment variables, for example
`ConnectionStrings__InventoryDatabase`.

Use `School__QrBaseUrl` and `School__WarrantyWarningDays` for environment overrides.
If the one-row `SchoolProfile` exists, its supported fields override these defaults.
Administrators manage the persisted profile through
`GET`/`PUT /api/v1/settings/school`; writes are versioned and audited.
Warranty state, days remaining, and expiring windows roll over at midnight in this
persisted school time zone rather than at UTC midnight.

Import/export limits are startup-validated. `MaxImportBytes` accepts 1 KB-50 MB,
`MaxImportRows` 1-10,000, `MaxExportRows` 1-10,000, `PreviewRows` 1-200, and
`PreviewErrors` 1-1,000. `LegacyDefaultCurrency` must contain three letters and both
`GeneratedAssetTagPrefix` must contain 1-50 ASCII letters/digits with only internal
hyphens. Rate-limit and observability thresholds must be positive. Multipart parsing permits
only 1 MB beyond the file limit for form envelope overhead; the application
independently bounds the bytes it reads.

Agent enrollment/check-in settings are startup-validated. The server accepts at most
a 1 MiB typed telemetry snapshot and rejects stale timestamps, stale queued
collection dates, nonce replays, non-increasing sequences, revoked identities and
invalid signatures. Configure the Windows service itself through its MSI-backed
`HKLM\SOFTWARE\Inventory\Agent` values; see
[`agent-deployment.md`](agent-deployment.md). Do not put the enrollment token in
`appsettings.json`, the registry or a machine environment variable. The installer's
explicitly hidden `ENROLLMENT_TOKEN` bootstrap property is the only direct-token
exception; it immediately stages machine-DPAPI-protected data and is intended for
individual installs or tightly controlled pilots.

The optional installer API is disabled when its two paths are blank. Paths must be
absolute local files with `.msi` and `.json` extensions. The API rejects missing
files, reparse-point files, malformed manifests, size/hash mismatches, and unsafe
download names. In Production it also refuses an unsigned or untrusted MSI. The
authenticated API is an administrative download surface, not a secret-delivery
channel. Windows Production verifies Authenticode directly. Linux Production requires the
detached release-manifest signature, an independently mounted trusted certificate,
and an approved version and SHA-256 pin; see `compose.agent-installer.yml`. The
trusted certificate cannot be loaded from the artifact directory. This verifies the
signed release record and exact MSI bytes, not the MSI's embedded Authenticode
signature on Linux. Keep GPO/SCCM as the normal mass-deployment channel.

Recommended production value:

```text
Data Source=/var/lib/inventory/inventory.db;Foreign Keys=True;Default Timeout=30;Pooling=True
```

Use an equivalent absolute Windows path when hosted on Windows. The application
normalises the path, creates its parent directory, forces foreign-key enforcement,
and uses at least a 30-second timeout. The default production example enables pooling.
It rejects `:memory:` and relative paths in Production.

Do not commit bootstrap passwords, certificates, or data-protection keys. Run one
write-capable application instance with the database on local durable storage. Protect
the database directory with service-account-only permissions and BitLocker, LUKS, or
equivalent volume encryption.

There is deliberately no built-in administrator password. Use
`scripts/seed-admin.ps1` interactively or inject `BootstrapAdmin__Email`,
`BootstrapAdmin__DisplayName`, and `BootstrapAdmin__Password` from a deployment secret
store for the one-shot command. Remove the password from the process environment
afterward.

Production startup write-probes the database parent, key ring, and backup root. It
rejects relative, temporary, deployed-application, UNC/network, removable, CD-ROM, or
RAM-disk paths and rejects nested key/backup roots. It also rejects a missing
data-protection path, wildcard CORS origin, loopback/non-HTTPS QR URL, and an
`AllowedHosts` value that is missing, wildcarded, or loopback-only. QR tokens and
Identity MFA material stored in the database are protected with this key ring; losing
it prevents existing labels from being rendered and invalidates protected MFA tokens.
Normal serving startup also requires a reachable, fully migrated database, then
transactionally reconciles role-preset claims before accepting requests. The switch
may be disabled only for isolated Development test/contract-generation hosts that
migrate and seed explicitly; Production rejects a disabled value.
Production also rejects disabled required-role MFA enforcement or a required-role
list that does not contain `Administrator`.

When the API runs behind a reverse proxy, set `ForwardedHeaders:Enabled=true` and list
the proxy addresses in `ForwardedHeaders:KnownProxies` (or networks in
`ForwardedHeaders:KnownNetworks`). Enabling forwarding without a trust entry, or
configuring trust entries while forwarding is disabled, fails startup.
Without this, every client shares the proxy address for rate limiting and audit
records. Headers from unlisted sources are ignored, so spoofed `X-Forwarded-For`
values from arbitrary clients are never trusted. On Windows the persisted
data-protection key ring is additionally encrypted with DPAPI unless
`Security:ProtectKeysWithDpapi` is set to `false`. Non-Windows Production requires a
configured PFX certificate; never store its password in JSON.

`Storage:RehearsalLoginPassword` is deliberately blank in source. Supply it only for
the one-shot rehearsal process (the helper prompts as a secure string) and remove it
afterward. Reports record the outcome but never the password. CSV/PDF exports are
streamed responses; there is no persistent attachment/export store to validate in
this backend milestone.
