using Inventory.Domain.Warranties;

namespace Inventory.Application.Warranties;

public sealed record WarrantyView(
    Guid Id,
    Guid AssetId,
    string Provider,
    string CoverageType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reference,
    string? Notes,
    bool IsPrimary,
    WarrantyState State,
    int? DaysRemaining,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    Guid Version);

public sealed record CreateWarrantyCommand(
    string Provider,
    string CoverageType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reference,
    string? Notes,
    bool IsPrimary);

public sealed record UpdateWarrantyCommand(
    string Provider,
    string CoverageType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reference,
    string? Notes,
    bool IsPrimary,
    Guid Version);
