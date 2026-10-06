# Asset register

## Implemented

- Create and retrieve by Guid or human asset tag
- Case-insensitive unique asset tag
- Optional unique serial number
- Category and location references
- Device name and description separate from the human asset tag and operator notes
- Manufacturer/model, serial, LAN MAC, BIOS GUID, ownership, status, condition,
  purchase data, supplier, operating system/version, nullable Windows 11 compliance,
  notes, and legacy ID
- Search across tag, device name, description, serial, LAN MAC, ownership,
  manufacturer, model, operating system, category, and location
- Search across current holder name/reference and filter by person, department, or
  assigned/unassigned state
- Filter by tag, serial, category, status, condition, location, warranty state/end
  date, created range, and legacy ID
- Primary warranty summary and safe per-asset QR image URL in list/detail responses
- Register list and detail responses expose every supported imported asset attribute
- Printed labels prefer the recorded device name and fall back to
  manufacturer/model or category when it is absent
- Stable allow-listed sorting and capped paging
- Full replacement update with optimistic concurrency
- Explicit retire/restore/dispose commands
- Append-only event history and notes

## Status rules

General asset create/update cannot set `assigned`; assignments own that transition.
Create and general update cannot set `retired` or `disposed`; dedicated lifecycle
operations own terminal state. Retired/disposed assets cannot be edited normally.

Retirement closes an open assignment, records its return snapshot, and appends custody
closure plus retirement events in the same database transaction.

Disposal records `disposedAtUtc` and `disposalReason` separately from any earlier
retirement metadata. It also closes open custody atomically. Disposal is terminal:
the asset cannot be restored or normally edited afterward.

## Event behavior

Create, details, status, condition, location, assignment, transfer, return, warranty,
QR issue/rotation, retire, restore, dispose, and note actions append plain-language events.
Structured JSON records changed values for a later UI without reconstructing old rows.
