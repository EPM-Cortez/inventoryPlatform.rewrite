using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.IntegrationTests;

public sealed class AssetTagAllocationPersistenceTests
{
    [Fact]
    public async Task ConcurrentReservationsNeverReturnDuplicateTags()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await InitializePolicyTableAsync(databasePath);

            var tags = await Task.WhenAll(
                Enumerable.Range(0, 20)
                    .Select(
                        async _ =>
                        {
                            await using var context = CreateContext(databasePath);
                            var repository = new EfAssetOperationsRepository(context);
                            return await repository.ReserveNextAssetTagAsync(
                                TestContext.Current.CancellationToken);
                        }));

            Assert.DoesNotContain(tags, tag => tag is null);
            Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("AST-000001", tags.Min(StringComparer.Ordinal));
            Assert.Equal("AST-000020", tags.Max(StringComparer.Ordinal));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ReservationParticipatesInExistingAgentStyleTransaction()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await InitializePolicyTableAsync(databasePath);
            await using (var context = CreateContext(databasePath))
            {
                var repository = new EfAssetOperationsRepository(context);
                await using var transaction = await repository.BeginTransactionAsync(
                    TestContext.Current.CancellationToken);
                var reserved = await repository.ReserveNextAssetTagAsync(
                    TestContext.Current.CancellationToken);
                Assert.Equal("AST-000001", reserved);
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            }

            await using var verificationContext = CreateContext(databasePath);
            var verificationRepository =
                new EfAssetOperationsRepository(verificationContext);
            var reservedAfterRollback =
                await verificationRepository.ReserveNextAssetTagAsync(
                    TestContext.Current.CancellationToken);
            Assert.Equal("AST-000001", reservedAfterRollback);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task InitializePolicyTableAsync(string databasePath)
    {
        await using var context = CreateContext(databasePath);
        await context.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE "AssetTagPolicies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AssetTagPolicies" PRIMARY KEY,
                "Prefix" TEXT NOT NULL,
                "NumericWidth" INTEGER NOT NULL,
                "NextNumber" INTEGER NOT NULL,
                "Version" TEXT NOT NULL
            );
            INSERT INTO "AssetTagPolicies"
                ("Id", "Prefix", "NumericWidth", "NextNumber", "Version")
            VALUES
                (1, 'AST-', 6, 1, '11111111-1111-1111-1111-111111111111');
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static InventoryDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(
                $"Data Source={databasePath};Default Timeout=30;Pooling=False")
            .Options;
        return new InventoryDbContext(options);
    }

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"inventory-asset-tags-{Guid.NewGuid():N}.db");
}
