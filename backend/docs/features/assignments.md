# People and assignments

Status: Implemented milestone 2.

## Holder records

People are separate from login users and contain an optional external ID, display name,
type, optional email, active flag, and concurrency version. Departments contain a
unique code, name, active flag, and version. Both support paged search, create, retrieve,
and versioned update. A holder with an open assignment cannot be made inactive.

External IDs and department codes are normalized before case-insensitive uniqueness
checks. Records are archived by setting `isActive`; normal persistence rejects deletes.

## Custody workflow

- `POST /api/v1/assets/{assetId}/assignments` assigns an available asset or transfers
  an already assigned asset. Exactly one of `personId` and `departmentId` is required.
- A caller supplies the last-seen asset version. A stale request returns `409`.
- Assign changes `InStock` or `Reserved` to `Assigned`.
- Transfer closes the previous period, opens the new period, rotates the asset version,
  and writes one `Transferred` event.
- `POST /api/v1/assets/{assetId}/assignments/{assignmentId}/return` requires both
  last-seen asset and assignment versions. It records server UTC time and actor,
  optionally applies condition/location, changes the asset to `InStock`, and writes a
  `Returned` event.
- Retirement closes any open assignment and appends both custody-closure and retirement
  history in the same unit of work.
- Repair, Lost, Stolen, Retired, and Disposed assets reject assignment.

EF Core commits the asset, old/new assignment periods, and asset event through one
`SaveChanges` transaction. SQLite independently enforces Person XOR Department with a
check constraint and zero-or-one open period with the filtered unique index
`UX_AssetAssignments_OpenAsset`.

## Queries

- Asset details and list items include `currentAssignment`.
- Asset search supports `departmentId`, `assignedPersonId`, and `assigned`.
- Assignment history is paged under the asset.
- Person and department asset endpoints return current custody by default and include
  closed periods when `includeHistory=true`.

Assignment rows keep target, actor, condition, location, note, and return snapshots so
history remains readable after a holder record is renamed.
