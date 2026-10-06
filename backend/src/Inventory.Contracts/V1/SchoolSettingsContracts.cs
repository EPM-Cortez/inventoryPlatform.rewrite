using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public sealed class UpdateSchoolSettingsRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string TimeZone { get; init; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string QrBaseUrl { get; init; } = string.Empty;

    [Range(1, 365)]
    public int WarrantyWarningDays { get; init; } = 60;

    public Guid? Version { get; init; }
}

public sealed record SchoolSettingsResponse(
    string Name,
    string Code,
    string TimeZone,
    string QrBaseUrl,
    int WarrantyWarningDays,
    bool IsPersisted,
    Guid? Version);
