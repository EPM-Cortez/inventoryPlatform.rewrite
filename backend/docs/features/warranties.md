# Warranties

Status: Implemented in milestone 3.

Warranty coverage retains provider, coverage type, optional start/end dates, reference,
notes, primary flag, timestamps, archive state, and a concurrency version. End cannot
precede start in both the Domain and SQLite check constraint. Records are archived,
never deleted.

## Calculated state

State is calculated at read time using the calendar date in the persisted
`SchoolProfile` time zone and warning threshold. Before a profile exists, the
`School:TimeZone` and `School:WarrantyWarningDays` defaults are used:

| State | Rule |
| --- | --- |
| `notRecorded` | No primary coverage, or its start and end are both absent |
| `future` | Start is after today |
| `expired` | End is before today |
| `expiringSoon` | Started (or no start) and end is today through the warning boundary |
| `active` | Started (or no start) and has no end or ends after the warning boundary |

The end date is inclusive, so coverage ending today is `expiringSoon`, not `expired`.
The default warning threshold is 60 days and valid configuration is 1-365. UTC
timestamps remain unchanged; only date-only warranty business rules roll over at
local school midnight. The same school business date drives warranty detail,
asset detail and list summaries, search filters, QR resolution, and CSV export.

## Endpoints

| Endpoint | Permission | Behaviour |
| --- | --- | --- |
| `GET /api/v1/assets/{assetId}/warranties` | `warranties.read` | Active records; `includeArchived=true` includes history |
| `POST /api/v1/assets/{assetId}/warranties` | `warranties.manage` | Adds coverage |
| `PUT /api/v1/warranties/{warrantyId}` | `warranties.manage` | Versioned update |
| `POST /api/v1/warranties/{warrantyId}/archive` | `warranties.manage` | Versioned archive |
| `GET /api/v1/warranties/expiring` | `warranties.read` | Paged inclusive expiry window; `withinDays` defaults to the configured threshold |

Setting a coverage primary demotes the prior primary in the same save. A filtered
unique index permits one non-archived primary coverage per asset. Asset detail and
list responses include the primary `warrantySummary`; asset search accepts
`warrantyState` and `warrantyEndsBefore`.

Add, update, and archive append structured `WarrantyAdded`, `WarrantyChanged`, and
`WarrantyArchived` asset events in the same unit of work. Stale versions return `409`
without overwriting newer coverage.
