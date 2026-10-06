using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Inventory.Contracts.V1;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SavedAssetFilterRequest
{
    [StringLength(200)]
    public string? Query { get; init; }

    [StringLength(100)]
    public string? AssetTag { get; init; }

    [StringLength(200)]
    public string? SerialNumber { get; init; }

    public Guid? CategoryId { get; init; }

    public AssetStatusContract? Status { get; init; }

    public AssetConditionContract? Condition { get; init; }

    public Guid? LocationId { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? AssignedPersonId { get; init; }

    public bool? Assigned { get; init; }

    public WarrantyStateContract? WarrantyState { get; init; }

    public DateOnly? WarrantyEndsBefore { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    [StringLength(200)]
    public string? LegacyId { get; init; }

    [Range(1, 200)]
    public int PageSize { get; init; } = 50;

    [StringLength(20)]
    public string Sort { get; init; } = "assetTag";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateSavedAssetSearchRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    public int SchemaVersion { get; init; } = 1;

    [Required]
    public SavedAssetFilterRequest Filter { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSavedAssetSearchRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    public int SchemaVersion { get; init; } = 1;

    [Required]
    public SavedAssetFilterRequest Filter { get; init; } = new();

    public Guid Version { get; init; }
}

public sealed class ChangeSavedAssetSearchRequest
{
    public Guid Version { get; init; }
}

public sealed record SavedAssetFilterResponse(
    string? Query,
    string? AssetTag,
    string? SerialNumber,
    Guid? CategoryId,
    AssetStatusContract? Status,
    AssetConditionContract? Condition,
    Guid? LocationId,
    Guid? DepartmentId,
    Guid? AssignedPersonId,
    bool? Assigned,
    WarrantyStateContract? WarrantyState,
    DateOnly? WarrantyEndsBefore,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    string? LegacyId,
    int PageSize,
    string Sort);

public sealed record SavedAssetSearchResponse(
    Guid Id,
    string Name,
    int SchemaVersion,
    SavedAssetFilterResponse Filter,
    bool IsDefault,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);
