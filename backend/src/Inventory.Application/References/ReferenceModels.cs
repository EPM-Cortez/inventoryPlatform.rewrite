namespace Inventory.Application.References;

public sealed record CategoryView(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid Version);

public sealed record LocationView(
    Guid Id,
    string Code,
    string Name,
    string? Type,
    Guid? ParentLocationId,
    bool IsActive,
    Guid Version);

public sealed record CreateCategoryCommand(string Code, string Name, string? Description);

public sealed record UpdateCategoryCommand(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid Version);

public sealed record CreateLocationCommand(
    string Code,
    string Name,
    string? Type,
    Guid? ParentLocationId);

public sealed record UpdateLocationCommand(
    string Code,
    string Name,
    string? Type,
    Guid? ParentLocationId,
    bool IsActive,
    Guid Version);
