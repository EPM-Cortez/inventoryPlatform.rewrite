using Microsoft.Data.Sqlite;
using Inventory.Infrastructure.Configuration;

namespace Inventory.Infrastructure.Data;

public static class SqliteDatabaseConfiguration
{
    private const int MinimumDefaultTimeoutSeconds = 30;

    public static string PrepareConnectionString(
        string connectionString,
        string basePath,
        bool requireProductionFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException(
                "The SQLite InventoryDatabase connection must specify Data Source.");
        }

        var isMemoryDatabase =
            builder.Mode == SqliteOpenMode.Memory ||
            string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
        if (requireProductionFile && isMemoryDatabase)
        {
            throw new InvalidOperationException(
                "Production requires a persistent SQLite database file.");
        }

        if (!isMemoryDatabase)
        {
            if (builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Use a normal absolute or relative filesystem path for the SQLite database.");
            }

            if (requireProductionFile && !Path.IsPathFullyQualified(builder.DataSource))
            {
                throw new InvalidOperationException(
                    "Production requires an absolute SQLite Data Source path.");
            }

            var fullPath = Path.IsPathFullyQualified(builder.DataSource)
                ? Path.GetFullPath(builder.DataSource)
                : Path.GetFullPath(builder.DataSource, basePath);
            if (requireProductionFile)
            {
                fullPath = ProductionPathValidator.PrepareDurableFilePath(
                    fullPath,
                    basePath,
                    "ConnectionStrings:InventoryDatabase Data Source");
            }

            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(
                    "The SQLite database path must include a directory.");

            Directory.CreateDirectory(directory);
            builder.DataSource = fullPath;
        }

        builder.ForeignKeys = true;
        builder.DefaultTimeout = Math.Max(
            builder.DefaultTimeout,
            MinimumDefaultTimeoutSeconds);
        return builder.ToString();
    }

    public static string? GetPersistentDatabasePath(string preparedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedConnectionString);
        var builder = new SqliteConnectionStringBuilder(preparedConnectionString);
        var isMemoryDatabase =
            builder.Mode == SqliteOpenMode.Memory ||
            string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
        return isMemoryDatabase
            ? null
            : Path.GetFullPath(builder.DataSource);
    }
}
