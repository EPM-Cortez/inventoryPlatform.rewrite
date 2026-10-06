# STR register sample analysis

Status: compatibility input reviewed on 2026-07-25. This is a sample-data profile,
not milestone-5 migration reconciliation or cutover sign-off.

## Files reviewed

| File | SHA-256 | Logical asset rows | Blank physical rows |
| --- | --- | ---: | ---: |
| `STR Asset Register - current1.csv` | `D99F9EAF8AF3927E412ADCF1C8C6DFEB17CE85990CA390F519AD7EE42F2F71D6` | 445 | 45 |
| `STR Asset Register - CLC.csv` | `973F2FB17F8CAB01952B8582007107429C0982316A96D19D2F2EA474B417A513` | 282 | 295 |

Both files use the same exact 20-column header. Each logical file is below the
default 500-row atomic import limit. Blank physical rows are ignored.

## Compatibility result

The backend now stores and returns all source asset attributes that belong on an
asset row. The detailed source-to-target mapping is in
[`../features/import-export.md`](../features/import-export.md).

- Device name, description, LAN MAC, BIOS GUID, ownership, OS, OS version, and
  nullable Windows 11 compliance were added to the asset model, API, search, labels,
  canonical CSV, and SQLite migration.
- Product/vendor, location, Used By, acquisition/cost, warranty dates, and asset state
  map to existing model concepts.
- The School column is source context for the one school profile. It is deliberately
  not persisted as a tenant or school key on every asset.
- All 329 nonblank LAN MAC values and 428 nonblank BIOS GUID values have valid syntax.
- Exact legacy dates using `dd/MM/yyyy` and `dd.MM.yyyy` are accepted. The 38
  year-only and 15 month/year pairs in `current1` use deterministic first/last-day
  boundaries, and its one Excel-serial date pair is decoded.
- Cost values use the configured legacy default currency, currently GBP.
- The 298 logical rows with a blank Device Name now receive stable generated asset
  tags during validation. Their `deviceName` remains blank so the import does not
  invent a hostname.

## Cleanup required before either full file can commit

Validation is intentionally all-or-nothing. It now records deterministic date
normalization and plans missing category/location creates, but the current source
snapshots still contain:

| Issue | current1 | CLC | Required decision/action |
| --- | ---: | ---: | --- |
| Duplicate nonblank serial groups | 1 (`4BS9FB3`) | 1 (`16843009`) | Correct the duplicate or record an approved exclusion |
| Blank School | 0 | 1 | Supply the reviewed single-school code |

For visibility, `current1` will generate 155 tags and CLC will generate 143. Of the
CLC rows, 142 have a serial and one has neither a source device name nor serial. The
generated tag is derived from the file fingerprint and row number rather than silently
treating the serial or description as the asset identity.

Across both files there are six normalized category values, 94 raw normalized
location values, and 20 Used By identifiers. Unmatched categories and locations are
shown as planned creates in the validation preview and are created atomically at
commit. The 94 raw locations include naming variants; importing the reviewed location
reference file first lets operators consolidate those names, while committing the
asset file directly intentionally creates each distinct unmatched value. Used By must
still resolve to an active person's external ID. The supplied reference-file profile
and exact-match counts are recorded in
[`../features/reference-data-import-export.md`](../features/reference-data-import-export.md).

The samples also contain one repeated MAC value and two repeated BIOS GUID values.
Those fields are not database uniqueness keys, so they do not block import, but they
must appear in the milestone-5 duplicate/reconciliation report.

`current1` has School `STR` on all 445 logical rows. `CLC` has `STR` on 281 rows and
one blank School value. The missing value should be reviewed during reconciliation;
it does not justify adding per-asset tenancy to this single-school service.
