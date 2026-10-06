namespace Inventory.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}

public sealed record CurrentActor(
    Guid UserId,
    string DisplayName,
    string? IpAddress,
    string? Client,
    string? CorrelationId);

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    CurrentActor RequireActor();

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }
}
