namespace Inventory.Infrastructure.Configuration;

public enum InventoryDatabaseProvider
{
    Sqlite,
    Postgres,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public InventoryDatabaseProvider Provider { get; init; } =
        InventoryDatabaseProvider.Sqlite;

    public string PasswordFile { get; init; } = string.Empty;
}
