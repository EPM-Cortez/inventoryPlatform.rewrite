namespace Inventory.Application.Abstractions;

public interface IDatabaseStorageWriteGuard
{
    void EnsureCapacity(long additionalBytes);
}
