using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public sealed class CreateCategoryRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }
}

public sealed class CreateLocationRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(80)]
    public string? Type { get; init; }

    public Guid? ParentLocationId { get; init; }
}

public sealed class UpdateCategoryRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    public bool IsActive { get; init; }

    public Guid Version { get; init; }
}

public sealed class UpdateLocationRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(80)]
    public string? Type { get; init; }

    public Guid? ParentLocationId { get; init; }

    public bool IsActive { get; init; }

    public Guid Version { get; init; }
}

public sealed record ReferenceSummaryResponse(Guid Id, string Code, string Name);

public sealed record LocationSummaryResponse(Guid Id, string Code, string Name);

public sealed record CategoryResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid Version);

public sealed record LocationResponse(
    Guid Id,
    string Code,
    string Name,
    string? Type,
    Guid? ParentLocationId,
    bool IsActive,
    Guid Version);
