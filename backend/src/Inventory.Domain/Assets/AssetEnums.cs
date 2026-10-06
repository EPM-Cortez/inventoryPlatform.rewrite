namespace Inventory.Domain.Assets;

public enum AssetStatus
{
    InStock,
    Reserved,
    Assigned,
    Repair,
    Lost,
    Stolen,
    Retired,
    Disposed,
}

public enum AssetCondition
{
    New,
    Good,
    Fair,
    Damaged,
    Unserviceable,
}

public enum AssetEventType
{
    AssetCreated,
    AssetDetailsChanged,
    StatusChanged,
    ConditionChanged,
    LocationChanged,
    Assigned,
    Transferred,
    Returned,
    WarrantyAdded,
    WarrantyChanged,
    WarrantyArchived,
    QrIssued,
    QrRotated,
    ImportCreated,
    ImportUpdated,
    Retired,
    Restored,
    Disposed,
    NoteAdded,
    ExpectedReturnChanged,
    AttachmentAdded,
    AttachmentArchived,
}

public enum AssetEventSource
{
    Api,
    Import,
    System,
    Migration,
}
