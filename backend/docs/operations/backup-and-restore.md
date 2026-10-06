# Backup and restore

Back up these stores as one recovery set:

1. the SQLite database, including all committed WAL state;
2. the ASP.NET Core data-protection key ring configured by
   `Security:DataProtectionKeysPath`.

SQLite is not encrypted by the application. Use service-account-only filesystem
permissions, encrypted fixed local storage, and encrypted backup media. Keep the
backup root separate from both the deployed application and key directory.

## Online backup command

`scripts/backup-inventory.ps1` runs the API in one-shot `--backup` mode. The service
uses SQLite's online backup API, so committed records in the live `-wal` file are
folded into a consistent standalone `inventory.db`; it never copies only the live main
file.

```powershell
$env:ConnectionStrings__InventoryDatabase =
  'Data Source=D:\InventoryData\inventory.db;Foreign Keys=True;Pooling=True'
$env:Security__DataProtectionKeysPath = 'D:\InventoryData\keys'
$env:Storage__BackupPath = 'E:\InventoryBackups'

.\scripts\backup-inventory.ps1
```

The command:

- checks the configured free-space floor;
- creates a staging directory below the backup root;
- takes the online database snapshot;
- copies every non-reparse-point key-ring file;
- records the release, applied migrations, all critical table counts (including
  Identity role, claim, login, and protected-token stores plus import, settings,
  legacy-reconciliation, agent-lifecycle, stocktake, bulk-change, tag-policy, bundle,
  leaver, attachment, and saved-search stores), byte lengths, SHA-256 hashes, UTC
  times, and duration in `manifest.json`; and
- atomically renames the staging directory to `backup-<id>`.

An error removes only the validated staging directory. A completed backup is never
silently overwritten.

## Isolated restore rehearsal

Use an enabled rehearsal account and ensure at least one active QR exists. The helper
prompts for the password without echo:

```powershell
.\scripts\rehearse-restore.ps1 `
  -LoginEmail 'restore-check@example.school'
```

One-shot `--rehearse-restore` selects the latest completed backup, verifies every
manifest hash, copies it into an isolated rehearsal directory, and performs:

- `PRAGMA integrity_check`;
- `PRAGMA foreign_key_check`;
- applied/pending migration comparison with the running release;
- recorded-versus-restored counts for every critical table;
- enabled Identity account/password verification;
- restored Administrator role and complete current permission-preset verification;
- restored MFA-enabled state plus unprotection of authenticator and recovery material
  with the restored key ring;
- restored key-ring unprotection of an active QR and token-hash comparison;
- known asset lookup, immutable asset-history read, and audit sample read; and
- representative Milestone 7 state, including stocktake/bulk/tag/bundle/leaver/saved
  search rows and at least one attachment whose restored BLOB length and SHA-256 are
  recalculated and matched.

It writes a pass/fail JSON report under `<BackupPath>\rehearsals`, including duration
and the rollback procedure, then removes the isolated restored files. It never
replaces the live database. The password and unprotected QR token are never written to
logs, manifests, or reports.

The automated representative WAL/DPAPI drill and measured timings are recorded in
[`restore-rehearsal-2026-07-25.md`](../evidence/milestone-5/restore-rehearsal-2026-07-25.md).
Repeat the same command against production-sized data, the production service
identity/certificate, and production storage before go-live and after material
schema, key-protection, host, or storage changes.

Attachments are stored in SQLite rather than a separate object store. Include realistic
invoice, warranty, photo, and disposal-certificate volume when sizing both the live
database and backup media. Monitor both volumes and keep retention long enough for
the school's evidence requirements. A metadata-only attachment check is insufficient:
the rehearsal must read the restored bytes and verify their recorded hash.

## Actual recovery

1. Stop the API and prevent all writes.
2. Preserve the failed database, WAL/SHM files, key directory, logs, and release ID
   for investigation.
3. Verify the selected manifest and all SHA-256 evidence.
4. Restore `inventory.db` and its matching `keys` directory to the configured durable
   paths with the service account's restrictive permissions.
5. Deploy the release recorded in the manifest and start one instance.
6. Confirm `/health/ready`, then repeat integrity, migration, login, asset/history,
   audit, and QR checks.
7. Reopen writes only after the operator records the result.

Never restore a database without its matching key ring. Doing so invalidates protected
cookies and prevents active QR image regeneration; protected Identity authenticator
and recovery material may also be unreadable. Do not weaken authentication or edit
the database ad hoc—use the supported bootstrap, reset, or controlled QR rotation
path.
