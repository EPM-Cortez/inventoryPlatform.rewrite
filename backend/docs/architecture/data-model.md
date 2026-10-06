# Data model

## Implemented model

```mermaid
erDiagram
    ASSET_CATEGORY ||--o{ ASSET : classifies
    LOCATION ||--o{ LOCATION : contains
    LOCATION ||--o{ ASSET : locates
    ASSET ||--o{ ASSET_ASSIGNMENT : has
    ASSET ||--o{ ASSET_ATTACHMENT : evidences
    ASSET ||--o{ QR_IDENTIFIER : identifies
    ASSET ||--o{ WARRANTY_COVERAGE : covers
    PERSON ||--o{ ASSET_ASSIGNMENT : receives
    PERSON ||--o{ LEAVER_CASE : has
    DEPARTMENT ||--o{ ASSET_ASSIGNMENT : receives
    ASSET_BUNDLE ||--|{ ASSET_BUNDLE_MEMBER : contains
    ASSET ||--o{ ASSET_BUNDLE_MEMBER : joins
    STOCKTAKE_SESSION ||--o{ STOCKTAKE_EXPECTED_ASSET : snapshots
    STOCKTAKE_SESSION ||--o{ STOCKTAKE_OBSERVATION : records
    ASSET ||--o{ STOCKTAKE_EXPECTED_ASSET : expected
    ASSET ||--o{ STOCKTAKE_OBSERVATION : observed
    ASSET ||--o{ ASSET_EVENT : records
    APPLICATION_USER ||--o{ ASSET_EVENT : performs
    APPLICATION_USER }o--o{ ROLE : belongs_to
    ROLE ||--o{ ROLE_CLAIM : grants
    APPLICATION_USER ||--o{ AUDIT_LOG : performs
    APPLICATION_USER ||--o{ ASSET_IMPORT_JOB : performs
    APPLICATION_USER ||--o{ SAVED_ASSET_SEARCH : owns
    APPLICATION_USER ||--o{ AGENT_ENROLLMENT_PROFILE : creates
    AGENT_ENROLLMENT_PROFILE ||--o{ MANAGED_AGENT : enrolls
    ASSET ||--o| MANAGED_AGENT : links
    MANAGED_AGENT ||--o{ MANAGED_AGENT_SNAPSHOT : reports

    ASSET {
        guid Id PK
        string AssetTag
        string AssetTagNormalized UK
        string DeviceName
        guid CategoryId FK
        string Description
        string Manufacturer
        string Model
        string SerialNumber
        string SerialNumberNormalized UK
        string LanMacAddress
        guid BiosGuid
        string Ownership
        string Status
        string Condition
        guid LocationId FK
        date PurchaseDate
        decimal PurchaseCost
        string Currency
        string OperatingSystem
        string OperatingSystemVersion
        bool Windows11Compliant
        datetime RetiredAtUtc
        string RetirementReason
        datetime DisposedAtUtc
        string DisposalReason
        datetime CreatedAtUtc
        datetime UpdatedAtUtc
        guid Version
    }

    ASSET_EVENT {
        guid Id PK
        guid AssetId FK
        string Type
        datetime OccurredAtUtc
        guid ActorUserId FK
        string Summary
        json DetailsJson
    }

    PERSON {
        guid Id PK
        string ExternalId UK
        string DisplayName
        string Type
        string Email
        bool IsActive
        guid Version
    }

    DEPARTMENT {
        guid Id PK
        string Code UK
        string Name
        bool IsActive
        guid Version
    }

    ASSET_ASSIGNMENT {
        guid Id PK
        guid AssetId FK
        guid PersonId FK
        guid DepartmentId FK
        datetime AssignedAtUtc
        date ExpectedReturnOn
        datetime ReturnedAtUtc
        guid AssignedByUserId FK
        guid ReturnedByUserId FK
        string AssignedCondition
        string ReturnCondition
        guid Version
    }

    ASSET_TAG_POLICY {
        int Id PK
        string Prefix
        int NumericWidth
        long NextNumber
        guid Version
    }

    ASSET_BULK_CHANGE_JOB {
        guid Id PK
        string OperationKind
        json OperationJson
        json ItemsJson
        string Reason
        string Fingerprint
        datetime ExpiresAtUtc
        datetime CommittedAtUtc
        guid Version
    }

    ASSET_BUNDLE {
        guid Id PK
        string Code UK
        string Name UK
        string Description
        datetime ArchivedAtUtc
        guid Version
    }

    ASSET_BUNDLE_MEMBER {
        guid Id PK
        guid BundleId FK
        guid AssetId FK
        string Role
        string OtherRole
        bool IsActive
    }

    STOCKTAKE_SESSION {
        guid Id PK
        string Name
        string Scope
        guid ScopeLocationId FK
        guid ScopeCategoryId FK
        string Status
        datetime StartedAtUtc
        datetime CompletedAtUtc
        datetime CancelledAtUtc
        guid Version
    }

    STOCKTAKE_EXPECTED_ASSET {
        guid Id PK
        guid SessionId FK
        guid AssetId FK
        string AssetTag
        string ExpectedStatus
        string ExpectedCondition
        guid ExpectedLocationId
    }

    STOCKTAKE_OBSERVATION {
        guid Id PK
        guid SessionId FK
        guid AssetId FK
        guid ObservedLocationId
        string ObservedCondition
        datetime ObservedAtUtc
    }

    LEAVER_CASE {
        guid Id PK
        guid PersonId FK
        date LeavingOn
        string Status
        datetime CreatedAtUtc
        datetime CompletedAtUtc
        datetime CancelledAtUtc
        guid Version
    }

    ASSET_ATTACHMENT {
        guid Id PK
        guid AssetId FK
        string Category
        string FileName
        string ContentType
        blob Content
        long SizeBytes
        string Sha256
        datetime ArchivedAtUtc
        guid Version
    }

    SAVED_ASSET_SEARCH {
        guid Id PK
        guid UserId FK
        string Name
        int SchemaVersion
        json FilterJson
        bool IsDefault
        datetime ArchivedAtUtc
        guid Version
    }

    QR_IDENTIFIER {
        guid Id PK
        guid AssetId FK
        string TokenHash UK
        string ProtectedToken
        int TokenVersion
        datetime IssuedAtUtc
        datetime RevokedAtUtc
        guid Version
    }

    WARRANTY_COVERAGE {
        guid Id PK
        guid AssetId FK
        string Provider
        string CoverageType
        date StartDate
        date EndDate
        bool IsPrimary
        datetime ArchivedAtUtc
        guid Version
    }

    APPLICATION_USER {
        guid Id PK
        string Email UK
        string DisplayName
        bool IsEnabled
        datetime ArchivedAtUtc
        guid Version
    }

    ASSET_IMPORT_JOB {
        guid Id PK
        int SchemaVersion
        string OriginalFileName
        string FileHash
        string SnapshotHash
        string Mode
        string Status
        int TotalRows
        int ValidRows
        int ErrorRows
        json ValidatedRowsJson
        json ErrorsJson
        guid CreatedByUserId FK
        datetime CreatedAtUtc
        guid CommittedByUserId FK
        datetime CommittedAtUtc
        guid Version
    }

    AGENT_ENROLLMENT_PROFILE {
        guid Id PK
        string TokenHash UK
        datetime ExpiresAtUtc
        int MaxEnrollments
        int EnrolledCount
        bool RequireTpm
        string AllowedHostnameSuffix
        datetime RevokedAtUtc
        guid Version
    }

    MANAGED_AGENT {
        guid Id PK
        guid EnrollmentProfileId FK
        guid LinkedAssetId FK_UK
        string DeviceIdNormalized UK
        string PublicKeyPem
        string KeyThumbprintSha256 UK
        string KeyProtection
        string HostnameNormalized
        long LastSequence
        datetime LastCheckInAtUtc
        datetime RevokedAtUtc
        guid Version
    }

    MANAGED_AGENT_SNAPSHOT {
        guid Id PK
        guid AgentId FK
        guid SnapshotId
        long Sequence
        string BodySha256
        json PayloadJson
        datetime CollectedAtUtc
        datetime ReceivedAtUtc
    }

    AGENT_REQUEST_NONCE {
        guid Id PK
        string Scope
        guid ContextId
        string NonceHash
        datetime ReceivedAtUtc
        datetime ExpiresAtUtc
    }
```

`SchoolProfile` is a one-row configuration entity. It is not copied as a foreign key
onto assets or other normal domain records.

## Integrity rules

- Normalized asset tags have a unique database index.
- Normalized non-null serial numbers have a filtered unique database index.
- Category deletion is restricted; location deletion sets an asset location to null.
- A location cannot be its own parent; application validation also prevents deeper
  parent cycles and an inactive parent with active children.
- A person external ID is unique when present; department codes are unique.
- Assignment target columns satisfy Person XOR Department.
- A filtered unique index permits at most one assignment with no return time per asset.
- Assignment expected-return dates are optional business dates. Overdue is derived
  only for open assignments against the persisted school timezone.
- The asset-tag policy is a versioned singleton. Its SQLite allocation advances the
  sequence atomically; changing the policy never rewrites existing asset tags.
- Bulk-change jobs retain the exact selection versions, normalized operation, reason,
  30-minute expiry, SHA-256 fingerprint, creator, and commit provenance. Commit
  revalidates the snapshot and persists asset changes/events/audit atomically.
- Bundle code and name are case-insensitively unique. A bundle contains at most 50
  distinct assets, and a filtered unique member index permits one active bundle per
  asset. Retired/disposed assets cannot join an active bundle.
- A stocktake's expected rows are an immutable start-time snapshot. Session versions
  protect changed observations and close/cancel actions; exact observation retries
  are idempotent. Observations never update `Asset`.
- A filtered unique index permits one open leaver case per person. Completion is
  application-gated on the person having no open assignments, while cancellation
  leaves custody unchanged.
- Attachment content is stored as a bounded SQLite BLOB with its server-detected type,
  byte length, and SHA-256. Content and evidence metadata are immutable; archive adds
  provenance and no hard-delete API exists.
- Saved-search names are unique among a user's active rows and a filtered unique index
  permits one active default per user. Repository queries are owner-scoped.
- Asset, assignment, person, department, category, location, user, and school profile
  versions are concurrency tokens.
- People, departments, assignments, and login accounts are retained rather than
  hard-deleted. Account deletion sets `ArchivedAtUtc`, disables login, and preserves
  historical foreign keys.
- QR token hashes are globally unique, `(AssetId, TokenVersion)` is unique, and a
  filtered unique index permits one non-revoked identifier per asset.
- Warranty dates have a database check constraint, and a filtered unique index permits
  one non-archived primary coverage per asset.
- QR identifiers are revoked and warranty records are archived rather than deleted.
- Asset and reference-data import jobs retain the file/snapshot hashes, normalized
  valid rows, complete error list, validation and commit actors/times, and
  reconciliation counts. Count and commit-state check constraints reject inconsistent
  persisted results.
- Both import-job types are concurrency-versioned and retained. Their user foreign
  keys restrict deletion so audit authorship remains resolvable.
- `LegacyReconciliationRun` is one-to-one with an asset import job and retains its
  deterministic report JSON, SHA-256 fingerprint, target count, and actor/time.
  `LegacyReconciliationSignOff` is one-to-one with a run and retains the immutable
  authenticated decision, exact fingerprint, exception rationales, notes, and
  signer/time. Neither type has an update/delete API, and the DbContext rejects
  modified/deleted tracked entries.
- Enrollment-profile token hashes, device IDs and signing-key thumbprints are unique.
  Profiles enforce expiry, device caps, revocation and optional TPM/hostname policy;
  the clear enrollment token is never persisted.
- `AgentKeyRecoveryAuthorization` binds a one-time hashed token to one agent, the
  approving Administrator, an exact agent concurrency version, TPM policy and a short
  expiry. Consumption/revocation are mutually exclusive and retained for audit; only
  one pending authorization can exist per agent.
- A filtered unique link permits no more than one managed agent per asset. Exact
  serial/BIOS auto-linking remains application-controlled and refuses ambiguity or
  terminal retired/disposed assets. Trust revocation atomically clears the link.
  Cross-table SQLite triggers also reject linking to a terminal asset and reject a
  terminal asset transition while a managed agent remains linked, closing the race
  after application preflight.
- Enrollment and check-in nonce hashes are reserved atomically by
  `(Scope, ContextId, NonceHash)` and expire after twice the accepted timestamp
  window. Only hashes are stored; expired rows are pruned during signed ingress.
- Agent snapshot ID and monotonic sequence are independently unique within an agent.
  Every accepted row is an append-only receipt retaining the signed body hash and a
  substantive content hash. Content hashing excludes volatile snapshot ID/collection
  time; unchanged inventory uses a null-payload heartbeat. Controlled count/byte
  retention preserves the newest detailed payload plus bounded recent receipts while
  deleting only older rows. Persisted short/long counters enforce per-agent ingestion
  capacity independently of retained-row count. Managed agents, enrollment profiles,
  and key-recovery authorizations cannot be hard-deleted.
- Asset events and audit logs are rejected if EF attempts to modify or delete them.
- Domain methods require UTC `DateTimeOffset`; SQLite persists it as UTC `DateTime`
  text so ordering remains server-side and deterministic.
- Money remains `decimal` in the Domain but is validated to two decimal places and
  stored as integer minor units plus a three-letter currency code.
- LAN MAC addresses are canonicalised to uppercase colon-separated octets. BIOS GUID
  and Windows 11 compliance are nullable because not every asset type exposes them.
- `SchoolProfile` remains global. The legacy register's School column is input context,
  not a foreign key or tenancy discriminator on each asset.

No placeholder tenancy columns are reserved. Any future legacy mapping extension must
remain evidence-linked to an import job rather than adding source-system fields to
every normal domain row.
