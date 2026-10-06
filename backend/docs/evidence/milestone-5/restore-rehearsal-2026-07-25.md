# Restore rehearsal evidence — 2026-07-25

## Scope

Representative local release drill for the milestone-5 backup/recovery path:

- Windows fixed local storage;
- .NET SDK 10.0.302 and runtime 10.0.10;
- file-backed SQLite in WAL mode;
- Windows DPAPI-protected ASP.NET Core data-protection key ring;
- enabled Administrator password account;
- representative asset, immutable history, audit row, and active protected QR.

This is repository acceptance evidence, not a claim that a production host or
production-sized dataset has been rehearsed. Repeat the operator command in the real
deployment environment before go-live.

## Execution

```powershell
C:\Users\Jack\.dotnet\dotnet.exe test `
  .\tests\Inventory.Api.IntegrationTests\Inventory.Api.IntegrationTests.csproj `
  --no-restore -m:1 /p:UseSharedCompilation=false `
  --filter FullyQualifiedName~BackupRestoreRehearsalTests
```

Observed structured operation timings:

| Operation | Duration |
| --- | ---: |
| SQLite online snapshot plus key-ring/manifest backup | 61 ms |
| Isolated restore and all verification checks | 205 ms |

## Passed checks

- committed WAL state captured through SQLite online backup;
- database and key files recorded with byte lengths and SHA-256 hashes;
- manifest hashes verified before restore;
- `PRAGMA integrity_check` returned `ok`;
- `PRAGMA foreign_key_check` returned no rows;
- all recorded migrations matched the current release and none were pending;
- eleven critical table counts matched the source manifest;
- enabled Identity account/password verified;
- active QR unprotected only with the restored key ring and reproduced its stored
  hash;
- the QR asset and its immutable history were readable;
- an immutable audit sample was readable;
- rollback procedure was present in the JSON report; and
- the isolated restored directory was removed after the report was written.

Automated regression:
[`BackupRestoreRehearsalTests.cs`](../../../tests/Inventory.Api.IntegrationTests/BackupRestoreRehearsalTests.cs).
