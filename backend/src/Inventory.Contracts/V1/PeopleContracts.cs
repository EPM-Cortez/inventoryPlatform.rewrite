using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum PersonTypeContract
{
    Staff,
    Pupil,
    Contractor,
    Other,
}

public class CreatePersonRequest
{
    [StringLength(100)]
    public string? ExternalId { get; init; }

    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    public PersonTypeContract Type { get; init; }

    [EmailAddress]
    [StringLength(320)]
    public string? Email { get; init; }
}

public sealed class UpdatePersonRequest : CreatePersonRequest
{
    public bool IsActive { get; init; } = true;

    public Guid Version { get; init; }
}

public sealed record PersonResponse(
    Guid Id,
    string? ExternalId,
    string DisplayName,
    PersonTypeContract Type,
    string? Email,
    bool IsActive,
    Guid Version);

public class CreateDepartmentRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;
}

public sealed class UpdateDepartmentRequest : CreateDepartmentRequest
{
    public bool IsActive { get; init; } = true;

    public Guid Version { get; init; }
}

public sealed record DepartmentResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    Guid Version);
