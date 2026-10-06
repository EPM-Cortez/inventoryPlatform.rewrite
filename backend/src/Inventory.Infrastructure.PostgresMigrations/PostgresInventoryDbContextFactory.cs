using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Infrastructure.PostgresMigrations;

public sealed class PostgresInventoryDbContextFactory
    : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__InventoryDatabase")
            ?? "Host=localhost;Port=5432;Database=inventory;Username=inventory;Password=inventory";
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(
                PostgresDatabaseConfiguration.ValidateConnectionString(
                    connectionString),
                postgres => postgres.MigrationsAssembly(
                    typeof(PostgresInventoryDbContextFactory).Assembly.FullName))
            .Options;
        return new InventoryDbContext(options);
    }
}
