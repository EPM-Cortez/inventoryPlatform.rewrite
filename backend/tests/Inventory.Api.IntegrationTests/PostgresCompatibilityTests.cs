using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Locations;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.Api.IntegrationTests;

public sealed class PostgresCompatibilityTests
{
    [Fact]
    public async Task RepresentativeBackendWorkflowsRunOnPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "INVENTORY_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.False(
                string.Equals(
                    Environment.GetEnvironmentVariable(
                        "INVENTORY_REQUIRE_POSTGRES_TESTS"),
                    bool.TrueString,
                    StringComparison.OrdinalIgnoreCase),
                "INVENTORY_POSTGRES_TEST_CONNECTION_STRING is required.");
            return;
        }

        await ResetDedicatedTestDatabaseAsync(connectionString);
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            postgresConnectionString: connectionString);
        var state = await factory.GetDatabaseStateAsync();
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            state.ProviderName);
        Assert.Empty(state.PendingMigrations);
        Assert.Contains(
            state.AppliedMigrations,
            migration => migration.EndsWith(
                "_AddPostgresInvariants",
                StringComparison.Ordinal));

        await new AssetApiTests(factory)
            .AdministratorCanCreateRetrieveAndAuditAnAsset();
        await new AssignmentApiTests(factory)
            .AssignTransferAndReturnPreserveAtomicCustodyHistory();
        await new AssetOperationsApiTests(factory)
            .ActiveBundleUniquenessReleasesOnArchiveAndRechecksOnRestore();
        await new DashboardStocktakeApiTests(factory)
            .StocktakeFreezesScopeAndReportsDiscrepanciesWithoutChangingAssets();
        await new ReferenceDataImportExportApiTests(factory)
            .SuppliedCompatibilityFormatsCommitExportAndRoundTrip();
        await new CustodyDocumentsApiTests(factory)
            .LeaverAndOverdueWorkflowNeverAutoReturnsCustody();
        await new CustodyDocumentsApiTests(factory)
            .AttachmentsAreMagicCheckedDownloadedAsAttachmentsAndArchived();
        await new CustodyDocumentsApiTests(factory)
            .SavedSearchesArePersonalAndHaveOneDefault();
        await new AgentApiTests(factory)
            .SignedAgentLifecycleLinksExactAssetAndRejectsRevokedKey();
        await AssertLocationGraphWriteSkewIsRejectedAsync(factory);
        await AssertImportPreviewConcurrentAdmissionAsync(factory);
    }

    private static async Task AssertImportPreviewConcurrentAdmissionAsync(
        InventoryApiFactory factory)
    {
        Guid administratorId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            administratorId = await database.Users
                .Where(user => user.Email == InventoryApiFactory.AdministratorEmail)
                .Select(user => user.Id)
                .SingleAsync();
        }

        async Task<bool> AdmitAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var budget = scope.ServiceProvider.GetRequiredService<EfImportPreviewBudget>();
            try
            {
                await budget.AdmitAsync(
                    Guid.NewGuid(),
                    ImportPreviewPayloadKind.Asset,
                    administratorId,
                    40 * 1024 * 1024,
                    DateTimeOffset.UtcNow,
                    async token =>
                    {
                        await database.SaveChangesAsync(token);
                        return true;
                    },
                    CancellationToken.None);
                return true;
            }
            catch (ResourceConflictException)
            {
                return false;
            }
            catch (PersistenceConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(AdmitAsync(), AdmitAsync());
        Assert.Equal(1, results.Count(accepted => accepted));
    }

    private static async Task AssertLocationGraphWriteSkewIsRejectedAsync(
        InventoryApiFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstLocation = Location.Create($"SSI-A-{suffix}", "SSI A");
        var secondLocation = Location.Create($"SSI-B-{suffix}", "SSI B");
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            setup.Locations.AddRange(firstLocation, secondLocation);
            await setup.SaveChangesAsync();
        }

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var firstRunner = firstScope.ServiceProvider
            .GetRequiredService<ILocationGraphMutationRunner>();
        var secondRunner = secondScope.ServiceProvider
            .GetRequiredService<ILocationGraphMutationRunner>();
        var bothRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;

        async Task<bool> ChangeParentAsync(
            InventoryDbContext context,
            ILocationGraphMutationRunner runner,
            Guid locationId,
            Guid parentId)
        {
            try
            {
                return await runner.RunAsync(
                    async token =>
                    {
                        var graph = await context.Locations
                            .AsNoTracking()
                            .Where(item =>
                                item.Id == firstLocation.Id ||
                                item.Id == secondLocation.Id)
                            .ToArrayAsync(token);
                        Assert.Equal(2, graph.Length);
                        if (Interlocked.Increment(ref readCount) == 2)
                        {
                            bothRead.TrySetResult();
                        }

                        await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                        var target = await context.Locations.SingleAsync(
                            item => item.Id == locationId,
                            token);
                        target.Update(
                            target.Code,
                            target.Name,
                            target.Type,
                            parentId,
                            isActive: true,
                            target.Version);
                        await context.SaveChangesAsync(token);
                        return true;
                    },
                    TestContext.Current.CancellationToken);
            }
            catch (PersistenceConflictException exception)
                when (exception.Code == "location_graph_conflict")
            {
                return false;
            }
        }

        var first = ChangeParentAsync(
            firstContext,
            firstRunner,
            firstLocation.Id,
            secondLocation.Id);
        var second = ChangeParentAsync(
            secondContext,
            secondRunner,
            secondLocation.Id,
            firstLocation.Id);
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, result => result);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stored = await verify.Locations
            .AsNoTracking()
            .Where(item =>
                item.Id == firstLocation.Id || item.Id == secondLocation.Id)
            .ToArrayAsync();
        Assert.Equal(2, stored.Length);
        Assert.Single(stored, item => item.ParentLocationId.HasValue);
    }

    private static async Task ResetDedicatedTestDatabaseAsync(
        string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database) ||
            !database.EndsWith(
                "_test",
                StringComparison.OrdinalIgnoreCase) ||
            database.Any(
                character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character != '_'))
        {
            throw new InvalidOperationException(
                "PostgreSQL compatibility tests only reset a database whose name ends in '_test'.");
        }

        NpgsqlConnection.ClearAllPools();
        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText =
                $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{database}\";";
        await create.ExecuteNonQueryAsync();
    }
}
