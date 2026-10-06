using Inventory.Domain.People;

namespace Inventory.Application.People;

public sealed record PersonSearch(
    string? Query,
    PersonType? Type,
    bool IncludeInactive,
    int Page,
    int PageSize,
    string Sort);

public sealed record PersonView(
    Guid Id,
    string? ExternalId,
    string DisplayName,
    PersonType Type,
    string? Email,
    bool IsActive,
    Guid Version);

public sealed record CreatePersonCommand(
    string? ExternalId,
    string DisplayName,
    PersonType Type,
    string? Email);

public sealed record UpdatePersonCommand(
    string? ExternalId,
    string DisplayName,
    PersonType Type,
    string? Email,
    bool IsActive,
    Guid Version);

public sealed record DepartmentSearch(
    string? Query,
    bool IncludeInactive,
    int Page,
    int PageSize,
    string Sort);

public sealed record DepartmentView(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    Guid Version);

public sealed record CreateDepartmentCommand(string Code, string Name);

public sealed record UpdateDepartmentCommand(
    string Code,
    string Name,
    bool IsActive,
    Guid Version);
