using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public class CreateWarrantyRequest
{
    [Required]
    [StringLength(200)]
    public string Provider { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string CoverageType { get; init; } = string.Empty;

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }

    public bool IsPrimary { get; init; }
}

public sealed class UpdateWarrantyRequest : CreateWarrantyRequest
{
    public Guid Version { get; init; }
}

public sealed record WarrantyResponse(
    Guid Id,
    Guid AssetId,
    string Provider,
    string CoverageType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reference,
    string? Notes,
    bool IsPrimary,
    WarrantyStateContract State,
    int? DaysRemaining,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    Guid Version);
