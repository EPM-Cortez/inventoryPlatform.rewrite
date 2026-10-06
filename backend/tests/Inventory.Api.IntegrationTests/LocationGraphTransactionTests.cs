using Inventory.Domain.Locations;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.IntegrationTests;

public sealed class LocationGraphTransactionTests
{
    [Fact]
    public async Task SqliteGraphReadAndWriteSerializeAcrossDifferentRows()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "inventory-location-graph-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "inventory.db");
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            var firstLocation = Location.Create("GRAPH-A", "Graph A");
            var secondLocation = Location.Create("GRAPH-B", "Graph B");
            await using (var setup = new InventoryDbContext(options))
            {
                await setup.Database.MigrateAsync();
                setup.Locations.AddRange(firstLocation, secondLocation);
                await setup.SaveChangesAsync();
            }

            await using var firstContext = new InventoryDbContext(options);
            await using var secondContext = new InventoryDbContext(options);
            var firstRunner = new EfLocationGraphMutationRunner(firstContext);
            var secondRunner = new EfLocationGraphMutationRunner(secondContext);
            var firstHasRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondHasEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var first = firstRunner.RunAsync(
                async token =>
                {
                    var location = await firstContext.Locations.SingleAsync(
                        item => item.Id == firstLocation.Id,
                        token);
                    firstHasRead.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                    location.Update(
                        location.Code,
                        location.Name,
                        location.Type,
                        secondLocation.Id,
                        isActive: true,
                        location.Version);
                    await firstContext.SaveChangesAsync(token);
                    return true;
                },
                TestContext.Current.CancellationToken);
            await firstHasRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = Task.Run(
                () => secondRunner.RunAsync(
                    async token =>
                    {
                        secondHasEntered.SetResult();
                        return await secondContext.Locations
                            .AsNoTracking()
                            .SingleAsync(
                                item => item.Id == firstLocation.Id,
                                token);
                    },
                    TestContext.Current.CancellationToken));
            try
            {
                await Task.Delay(150, TestContext.Current.CancellationToken);
                Assert.False(secondHasEntered.Task.IsCompleted);
            }
            finally
            {
                releaseFirst.TrySetResult();
            }

            Assert.True(await first);
            var observedAfterCommit = await second;
            Assert.Equal(secondLocation.Id, observedAfterCommit.ParentLocationId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
