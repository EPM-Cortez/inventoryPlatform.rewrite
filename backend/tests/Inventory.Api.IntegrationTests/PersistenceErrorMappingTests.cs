using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.IntegrationTests;

public sealed class PersistenceErrorMappingTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private InventoryDbContext _dbContext = null!;
    private EfInventoryRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new InventoryDbContext(options);
        await _dbContext.Database.MigrateAsync();
        _repository = new EfInventoryRepository(
            _dbContext,
            new SystemClock(),
            new StubSchoolSettingsProvider());
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UniqueConstraintViolationMapsToDataConflictWithRootCause()
    {
        _repository.AddCategory(AssetCategory.Create("MAP-DUP", "First category"));
        await _repository.SaveChangesAsync(CancellationToken.None);
        _repository.AddCategory(AssetCategory.Create("map-dup", "Duplicate category"));

        var mapped = await Assert.ThrowsAsync<PersistenceConflictException>(
            () => _repository.SaveChangesAsync(CancellationToken.None));

        Assert.Equal("data_conflict", mapped.Code);
        var root = Assert.IsType<SqliteException>(
            mapped.InnerException?.GetBaseException());
        Assert.Equal(19, root.SqliteErrorCode);
    }

    [Fact]
    public async Task NonConstraintDatabaseFailurePropagatesUnmapped()
    {
        await _dbContext.Database.ExecuteSqlRawAsync("""DROP TABLE "AssetCategories";""");
        _repository.AddCategory(AssetCategory.Create("MAP-ENV", "Environmental failure"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => _repository.SaveChangesAsync(CancellationToken.None));

        var root = Assert.IsType<SqliteException>(exception.GetBaseException());
        Assert.NotEqual(19, root.SqliteErrorCode);
    }

    private sealed class StubSchoolSettingsProvider : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "Test School",
                    "TEST",
                    "Europe/London",
                    "https://inventory.test/scan",
                    60));
    }
}
