using Npgsql;

namespace Inventory.Infrastructure.Data;

public static class PostgresDatabaseConfiguration
{
    public const string MigrationsAssemblyName =
        "Inventory.Infrastructure.PostgresMigrations";

    public static string PrepareConnectionString(
        string connectionString,
        string? passwordFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The PostgreSQL InventoryDatabase connection string is invalid.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(builder.Host) ||
            string.IsNullOrWhiteSpace(builder.Database) ||
            string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException(
                "The PostgreSQL InventoryDatabase connection must specify Host, Database, and Username.");
        }

        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!Path.IsPathFullyQualified(passwordFile))
            {
                throw new InvalidOperationException(
                    "Database:PasswordFile must be an absolute path.");
            }

            var fullPath = Path.GetFullPath(passwordFile);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    "Database:PasswordFile must identify an existing secret file.");
            }

            var password = File.ReadAllText(fullPath).TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(password) || password.Length > 1024)
            {
                throw new InvalidOperationException(
                    "Database:PasswordFile must contain a non-empty password of at most 1024 characters.");
            }

            builder.Password = password;
        }

        if (builder.Timeout < 15)
        {
            builder.Timeout = 15;
        }

        if (builder.CommandTimeout < 30)
        {
            builder.CommandTimeout = 30;
        }

        return builder.ConnectionString;
    }

    public static string ValidateConnectionString(string connectionString) =>
        PrepareConnectionString(connectionString, passwordFile: null);
}
