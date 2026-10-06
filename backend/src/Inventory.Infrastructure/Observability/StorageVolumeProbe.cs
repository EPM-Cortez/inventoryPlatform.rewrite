namespace Inventory.Infrastructure.Observability;

public sealed record DatabaseStorageLocation(string? DatabasePath);

public interface IStorageVolumeProbe
{
    long GetAvailableFreeSpaceBytes(string path);
}

public sealed class StorageVolumeProbe : IStorageVolumeProbe
{
    public long GetAvailableFreeSpaceBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("The storage path has no filesystem root.");
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }
}
