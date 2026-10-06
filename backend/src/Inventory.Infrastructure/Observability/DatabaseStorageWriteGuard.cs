using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Observability;

public sealed class DatabaseStorageWriteGuard : IDatabaseStorageWriteGuard
{
    private readonly DatabaseStorageLocation _location;
    private readonly IStorageVolumeProbe _volumeProbe;
    private readonly StorageOptions _options;

    public DatabaseStorageWriteGuard(
        DatabaseStorageLocation location,
        IStorageVolumeProbe volumeProbe,
        IOptions<StorageOptions> options)
    {
        _location = location;
        _volumeProbe = volumeProbe;
        _options = options.Value;
    }

    public void EnsureCapacity(long additionalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalBytes);
        if (string.IsNullOrWhiteSpace(_location.DatabasePath))
        {
            return;
        }

        var available = _volumeProbe.GetAvailableFreeSpaceBytes(
            _location.DatabasePath);
        if (available - additionalBytes <
            _options.MinimumDatabaseFreeSpaceBytes)
        {
            throw new ResourceConflictException(
                "database_storage_capacity_low",
                "The database volume does not have enough free space for this write.");
        }
    }
}
