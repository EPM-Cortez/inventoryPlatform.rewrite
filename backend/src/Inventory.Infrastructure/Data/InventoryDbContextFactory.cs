using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Infrastructure.Data;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDatabase")
            ?? "Data Source=data/inventory.db;Foreign Keys=True;Default Timeout=30;Pooling=True";
        var contentRoot = FindApiContentRoot();
        var sqliteConnectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
            connectionString,
            contentRoot,
            requireProductionFile: false);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(
                sqliteConnectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
            .Options;

        return new InventoryDbContext(options);
    }

    private static string FindApiContentRoot()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var fromSolutionRoot = Path.Combine(currentDirectory, "src", "Inventory.Api");
        if (Directory.Exists(fromSolutionRoot))
        {
            return fromSolutionRoot;
        }

        var siblingApiProject = Path.GetFullPath(
            Path.Combine(currentDirectory, "..", "Inventory.Api"));
        return Directory.Exists(siblingApiProject)
            ? siblingApiProject
            : currentDirectory;
    }
}
