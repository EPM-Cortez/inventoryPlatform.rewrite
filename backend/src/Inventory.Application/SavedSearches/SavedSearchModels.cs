using Inventory.Domain.Assets;
using Inventory.Domain.Warranties;

namespace Inventory.Application.SavedSearches;

public sealed record SavedAssetFilter(
    string? Query,
    string? AssetTag,
    string? SerialNumber,
    Guid? CategoryId,
    AssetStatus? Status,
    AssetCondition? Condition,
    Guid? LocationId,
    Guid? DepartmentId,
    Guid? AssignedPersonId,
    bool? Assigned,
    WarrantyState? WarrantyState,
    DateOnly? WarrantyEndsBefore,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    string? LegacyId,
    int PageSize,
    string Sort);

public sealed record CreateSavedAssetSearchCommand(
    string Name,
    int SchemaVersion,
    SavedAssetFilter Filter);

public sealed record UpdateSavedAssetSearchCommand(
    string Name,
    int SchemaVersion,
    SavedAssetFilter Filter,
    Guid Version);

public sealed record ChangeSavedAssetSearchCommand(Guid Version);

public sealed record SavedAssetSearchRecord(
    Guid Id,
    string Name,
    int SchemaVersion,
    string FilterJson,
    bool IsDefault,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);

public sealed record SavedAssetSearchView(
    Guid Id,
    string Name,
    int SchemaVersion,
    SavedAssetFilter Filter,
    bool IsDefault,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);
