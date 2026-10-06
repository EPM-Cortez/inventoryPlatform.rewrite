using System.Globalization;
using Inventory.Domain.Agents;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Bundles;
using Inventory.Domain.People;
using Inventory.Domain.School;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.IntegrationTests;

public sealed class SqliteMigrationTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string ApiContentRoot = Path.Combine(
        RepositoryRoot,
        "src",
        "Inventory.Api");

    [Fact]
    public async Task UserLifecycleMigrationVersionsExistingAccounts()
    {
        var testDirectory = NewTestDirectory(
            "user-migration");
        var databasePath = Path.Combine(testDirectory, "inventory.db");

        try
        {
            var connectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
                $"Data Source={databasePath};Pooling=False",
                ApiContentRoot,
                requireProductionFile: true);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite =>
                        sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
                .Options;
            var userId = Guid.NewGuid();

            await using var dbContext = new InventoryDbContext(options);
            await dbContext.Database.MigrateAsync(
                "20260724230241_AddQrIdentifiersAndWarranties");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Users"
                    ("Id", "DisplayName", "IsEnabled", "MustChangePassword",
                     "CreatedAtUtc", "EmailConfirmed", "PhoneNumberConfirmed",
                     "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES
                    ({userId}, 'Existing Administrator', 1, 0,
                     {DateTime.UtcNow}, 1, 0, 0, 1, 0);
                """);

            await dbContext.Database.MigrateAsync();
            dbContext.ChangeTracker.Clear();

            var migrated = await dbContext.Users
                .AsNoTracking()
                .SingleAsync(user => user.Id == userId);
            Assert.Equal(userId, migrated.Version);
            Assert.Null(migrated.ArchivedAtUtc);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionSqliteMigrationsCreateAReadyWalDatabase()
    {
        var testDirectory = NewTestDirectory(
            "production-migration");
        var databasePath = Path.Combine(testDirectory, "inventory.db");

        try
        {
            var connectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
                $"Data Source={databasePath};Pooling=False",
                ApiContentRoot,
                requireProductionFile: true);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite =>
                        sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
                .Options;

            await using var dbContext = new InventoryDbContext(options);
            await dbContext.Database.MigrateAsync();

            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.Contains(
                await dbContext.Database.GetAppliedMigrationsAsync(),
                migration =>
                    migration.EndsWith(
                        "_ConfigureSqliteJournal",
                        StringComparison.Ordinal));

            await dbContext.Database.OpenConnectionAsync();
            Assert.Equal("wal", await ReadPragmaAsync(dbContext, "journal_mode"));
            Assert.Equal("1", await ReadPragmaAsync(dbContext, "foreign_keys"));
            Assert.Contains(
                "\"ReturnedAtUtc\" IS NULL",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "index",
                    "UX_AssetAssignments_OpenAsset"),
                StringComparison.Ordinal);
            Assert.Contains(
                "CK_AssetAssignments_ExactlyOneTarget",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "table",
                    "AssetAssignments"),
                StringComparison.Ordinal);
            Assert.Contains(
                "CK_AssetAssignments_ExpectedReturnOn",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "table",
                    "AssetAssignments"),
                StringComparison.Ordinal);
            Assert.Contains(
                "inactive_assignment_person",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_AssetAssignments_ActivePerson_Insert"),
                StringComparison.Ordinal);
            Assert.Contains(
                "person_has_open_assignments",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_People_BlockDeactivateWithOpenAssignments"),
                StringComparison.Ordinal);
            Assert.Contains(
                "\"RevokedAtUtc\" IS NULL",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "index",
                    "UX_QrIdentifiers_ActiveAsset"),
                StringComparison.Ordinal);
            Assert.Contains(
                "CK_QrIdentifiers_RevocationFields",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "table",
                    "QrIdentifiers"),
                StringComparison.Ordinal);
            Assert.Contains(
                "\"IsPrimary\" = 1",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "index",
                    "UX_WarrantyCoverages_ActivePrimary"),
                StringComparison.Ordinal);
            Assert.Contains(
                "CK_WarrantyCoverages_DateRange",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "table",
                    "WarrantyCoverages"),
                StringComparison.Ordinal);
            Assert.Contains(
                "leaver_has_open_custody",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_LeaverCases_BlockCompletionWithOpenCustody"),
                StringComparison.Ordinal);
            Assert.Contains(
                "saved_search_limit_reached",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_SavedAssetSearches_LimitActivePerUser"),
                StringComparison.Ordinal);
            Assert.Contains(
                "assignment_target_is_leaver",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_AssetAssignments_BlockLeaverTarget_Insert"),
                StringComparison.Ordinal);
            Assert.Contains(
                "asset_has_active_bundle",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_Assets_BlockTerminalStatusWithBundle"),
                StringComparison.Ordinal);
            Assert.Contains(
                "attachment_quota_exceeded",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_AssetAttachments_LimitPerAsset"),
                StringComparison.Ordinal);
            Assert.Contains(
                "bulk_preview_limit_reached",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_AssetBulkChangeJobs_LimitActivePerUser"),
                StringComparison.Ordinal);
            Assert.Contains(
                "stocktake_active_limit_reached",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_StocktakeSessions_LimitInProgress"),
                StringComparison.Ordinal);
            Assert.Contains(
                "stocktake_duplicate_active",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_StocktakeSessions_BlockDuplicateInProgress"),
                StringComparison.Ordinal);
            Assert.Contains(
                "school_profile_singleton",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "trigger",
                    "TR_SchoolProfile_Singleton_Insert"),
                StringComparison.Ordinal);
            Assert.Contains(
                "CK_Users_ArchivedDisabled",
                await ReadSchemaSqlAsync(
                    dbContext,
                    "table",
                    "Users"),
                StringComparison.Ordinal);
            await dbContext.Database.CloseConnectionAsync();

            dbContext.SchoolProfiles.Add(
                SchoolProfile.Create(
                    "Database Test School",
                    "DB",
                    "Europe/London",
                    new Uri("https://inventory.database.test/scan"),
                    60));
            await dbContext.SaveChangesAsync();
            dbContext.SchoolProfiles.Add(
                SchoolProfile.Create(
                    "Second School",
                    "DB2",
                    "Europe/London",
                    new Uri("https://inventory-2.database.test/scan"),
                    60));
            var singletonException = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            Assert.Contains(
                "school_profile_singleton",
                singletonException.InnerException?.Message ?? string.Empty,
                StringComparison.Ordinal);
            dbContext.ChangeTracker.Clear();

            var category = AssetCategory.Create("DB-TEST", "Database test");
            var asset = Asset.Create(
                new AssetDetails(
                    "DB-ASSET",
                    category.Id,
                    null,
                    null,
                    null,
                    AssetStatus.InStock,
                    AssetCondition.Good,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                DateTimeOffset.UtcNow);
            var person = Person.Create("DB-PERSON", "Database Person", PersonType.Other);
            var actor = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "database-test@example.test",
                NormalizedUserName = "DATABASE-TEST@EXAMPLE.TEST",
                Email = "database-test@example.test",
                NormalizedEmail = "DATABASE-TEST@EXAMPLE.TEST",
                DisplayName = "Database Test",
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            dbContext.AssetCategories.Add(category);
            dbContext.Assets.Add(asset);
            dbContext.People.Add(person);
            dbContext.Users.Add(actor);
            await dbContext.SaveChangesAsync();
            var archivedEnabledException = await Assert.ThrowsAsync<SqliteException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE "Users"
                    SET "ArchivedAtUtc" = {DateTime.UtcNow}, "IsEnabled" = 1
                    WHERE "Id" = {actor.Id};
                    """));
            Assert.Contains(
                "CK_Users_ArchivedDisabled",
                archivedEnabledException.Message,
                StringComparison.Ordinal);

            var firstAssignment = AssetAssignment.Create(
                asset.Id,
                person.Id,
                null,
                person.DisplayName,
                person.ExternalId,
                DateTimeOffset.UtcNow,
                actor.Id,
                actor.DisplayName,
                asset.Condition,
                asset.LocationId);
            var secondAssignment = AssetAssignment.Create(
                asset.Id,
                person.Id,
                null,
                person.DisplayName,
                person.ExternalId,
                DateTimeOffset.UtcNow,
                actor.Id,
                actor.DisplayName,
                asset.Condition,
                asset.LocationId);
            dbContext.AssetAssignments.AddRange(firstAssignment, secondAssignment);
            await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            dbContext.ChangeTracker.Clear();

            var initialMigration = (await dbContext.Database.GetAppliedMigrationsAsync())
                .Single(
                    migration =>
                        migration.EndsWith(
                            "_InitialCreate",
                            StringComparison.Ordinal));
            await dbContext.Database.MigrateAsync(initialMigration);
            await dbContext.Database.OpenConnectionAsync();
            Assert.Equal("delete", await ReadPragmaAsync(dbContext, "journal_mode"));
            await dbContext.Database.CloseConnectionAsync();
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                foreach (var file in Directory.GetFiles(testDirectory))
                {
                    File.Delete(file);
                }

                Directory.Delete(testDirectory);
            }
        }
    }

    [Fact]
    public async Task Milestone7TriggersCloseLeaverAndSavedSearchRaces()
    {
        var testDirectory = NewTestDirectory("milestone7-races");
        var databasePath = Path.Combine(testDirectory, "inventory.db");

        try
        {
            var connectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
                $"Data Source={databasePath};Pooling=False",
                ApiContentRoot,
                requireProductionFile: true);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite =>
                        sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
                .Options;
            var now = DateTimeOffset.UtcNow;
            var actor = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "milestone7-races@example.test",
                NormalizedUserName = "MILESTONE7-RACES@EXAMPLE.TEST",
                Email = "milestone7-races@example.test",
                NormalizedEmail = "MILESTONE7-RACES@EXAMPLE.TEST",
                DisplayName = "Milestone 7 Race Test",
                IsEnabled = true,
                CreatedAtUtc = now,
            };
            var category = AssetCategory.Create("M7-RACE", "Milestone 7 race");
            var asset = Asset.Create(
                CreateAssetDetails("DB-M7-RACE", category.Id),
                now);
            var person = Person.Create(
                "DB-M7-PERSON",
                "Milestone 7 Person",
                PersonType.Other);

            await using var dbContext = new InventoryDbContext(options);
            await dbContext.Database.MigrateAsync();
            asset.Assign(asset.Version, now);
            dbContext.AddRange(actor, category, asset, person);
            dbContext.AssetAssignments.Add(
                AssetAssignment.Create(
                    asset.Id,
                    person.Id,
                    null,
                    person.DisplayName,
                    person.ExternalId,
                    now,
                    actor.Id,
                    actor.DisplayName,
                    asset.Condition,
                    asset.LocationId));
            await dbContext.SaveChangesAsync();

            var leaverCase = LeaverCase.Create(
                person.Id,
                DateOnly.FromDateTime(now.UtcDateTime).AddDays(7),
                "Race-proof completion.",
                now,
                actor.Id,
                actor.DisplayName);
            dbContext.LeaverCases.Add(leaverCase);
            await dbContext.SaveChangesAsync();

            leaverCase.Complete(
                leaverCase.Version,
                now.AddMinutes(1),
                actor.Id,
                actor.DisplayName);
            var custodyException = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            Assert.Contains(
                "leaver_has_open_custody",
                custodyException.GetBaseException().Message,
                StringComparison.Ordinal);
            dbContext.ChangeTracker.Clear();
            Assert.Equal(
                LeaverCaseStatus.Open,
                (await dbContext.LeaverCases.SingleAsync(
                    item => item.Id == leaverCase.Id)).Status);

            var trackedAsset = await dbContext.Assets.SingleAsync(
                item => item.Id == asset.Id);
            var trackedAssignment = await dbContext.AssetAssignments.SingleAsync(
                item => item.AssetId == asset.Id && item.ReturnedAtUtc == null);
            trackedAsset.Return(
                trackedAsset.Version,
                now.AddMinutes(2),
                condition: null,
                locationId: null);
            trackedAssignment.Close(
                trackedAssignment.Version,
                now.AddMinutes(2),
                actor.Id,
                actor.DisplayName,
                trackedAsset.Condition,
                trackedAsset.LocationId);
            await dbContext.SaveChangesAsync();

            var trackedLeaverCase = await dbContext.LeaverCases.SingleAsync(
                item => item.Id == leaverCase.Id);
            trackedLeaverCase.Complete(
                trackedLeaverCase.Version,
                now.AddMinutes(3),
                actor.Id,
                actor.DisplayName);
            await dbContext.SaveChangesAsync();

            trackedAsset.Assign(trackedAsset.Version, now.AddMinutes(4));
            dbContext.AssetAssignments.Add(
                AssetAssignment.Create(
                    trackedAsset.Id,
                    person.Id,
                    null,
                    person.DisplayName,
                    person.ExternalId,
                    now.AddMinutes(4),
                    actor.Id,
                    actor.DisplayName,
                    trackedAsset.Condition,
                    trackedAsset.LocationId));
            var reassignmentException = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            Assert.Contains(
                "assignment_target_is_leaver",
                reassignmentException.GetBaseException().Message,
                StringComparison.Ordinal);
            dbContext.ChangeTracker.Clear();
            Assert.Equal(
                AssetStatus.InStock,
                (await dbContext.Assets.SingleAsync(
                    item => item.Id == asset.Id)).Status);

            for (var index = 1; index <= 50; index++)
            {
                dbContext.SavedAssetSearches.Add(
                    SavedAssetSearch.Create(
                        actor.Id,
                        $"Saved search {index}",
                        schemaVersion: 1,
                        filterJson: "{}",
                        now.AddMinutes(index)));
            }

            await dbContext.SaveChangesAsync();
            dbContext.SavedAssetSearches.Add(
                SavedAssetSearch.Create(
                    actor.Id,
                    "Saved search 51",
                    schemaVersion: 1,
                    filterJson: "{}",
                    now.AddHours(1)));
            var limitException = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            Assert.Contains(
                "saved_search_limit_reached",
                limitException.GetBaseException().Message,
                StringComparison.Ordinal);
            dbContext.ChangeTracker.Clear();
            Assert.Equal(
                50,
                await dbContext.SavedAssetSearches.CountAsync(
                    item => item.UserId == actor.Id &&
                            item.ArchivedAtUtc == null));

            var bundle = AssetBundle.Create(
                "DB-M7-BUNDLE",
                "Milestone 7 bundle",
                null,
                [
                    new AssetBundleMemberSpec(
                        asset.Id,
                        AssetBundleMemberRole.Laptop,
                        null),
                ],
                now.AddHours(2));
            dbContext.AssetBundles.Add(bundle);
            await dbContext.SaveChangesAsync();
            var terminalException = await Assert.ThrowsAsync<SqliteException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE "Assets"
                    SET "Status" = 'Retired'
                    WHERE "Id" = {asset.Id};
                    """));
            Assert.Contains(
                "asset_has_active_bundle",
                terminalException.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AgentAssetLifecycleTriggersCloseTerminalLinkRaces()
    {
        var testDirectory = NewTestDirectory("agent-asset-lifecycle");
        var databasePath = Path.Combine(testDirectory, "inventory.db");

        try
        {
            var connectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
                $"Data Source={databasePath};Pooling=False",
                ApiContentRoot,
                requireProductionFile: true);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite =>
                        sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
                .Options;
            var now = DateTimeOffset.UtcNow;
            var actor = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "agent-lifecycle@example.test",
                NormalizedUserName = "AGENT-LIFECYCLE@EXAMPLE.TEST",
                Email = "agent-lifecycle@example.test",
                NormalizedEmail = "AGENT-LIFECYCLE@EXAMPLE.TEST",
                DisplayName = "Agent Lifecycle Test",
                IsEnabled = true,
                CreatedAtUtc = now,
            };
            var category = AssetCategory.Create(
                "AGENT-LIFECYCLE",
                "Agent lifecycle");
            var asset = Asset.Create(
                CreateAssetDetails("DB-AGENT-LIFECYCLE", category.Id),
                now);
            var profile = AgentEnrollmentProfile.Create(
                Guid.NewGuid(),
                "Agent lifecycle",
                Convert.ToBase64String(new byte[32]),
                now.AddHours(1),
                1,
                requireTpm: true,
                allowedHostnameSuffix: null,
                actor.Id,
                now);
            var agent = ManagedAgent.Enroll(
                profile.Id,
                "db-agent-lifecycle",
                "test-public-key",
                new string('a', 64),
                AgentKeyProtection.Tpm,
                "db-agent-lifecycle.example.test",
                "DB-AGENT-SERIAL",
                Guid.NewGuid().ToString(),
                "Example",
                "Test",
                now);
            agent.LinkAsset(
                asset.Id,
                "Database invariant test",
                agent.Version);

            await using var dbContext = new InventoryDbContext(options);
            await dbContext.Database.MigrateAsync();
            dbContext.AddRange(actor, category, asset, profile, agent);
            await dbContext.SaveChangesAsync();

            var terminalWithLink = await Assert.ThrowsAsync<SqliteException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE "Assets"
                    SET "Status" = 'Retired'
                    WHERE "Id" = {asset.Id};
                    """));
            Assert.Contains(
                "asset_has_linked_agent",
                terminalWithLink.Message,
                StringComparison.Ordinal);

            agent.UnlinkAsset("Prepare terminal-link test", agent.Version);
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Assets"
                SET "Status" = 'Retired'
                WHERE "Id" = {asset.Id};
                """);

            var linkToTerminal = await Assert.ThrowsAsync<SqliteException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE "ManagedAgents"
                    SET "LinkedAssetId" = {asset.Id}
                    WHERE "Id" = {agent.Id};
                    """));
            Assert.Contains(
                "agent_asset_terminal",
                linkToTerminal.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileBackedSqliteRollsBackCustodyWritesAndRejectsInvalidFinalStates()
    {
        var testDirectory = NewTestDirectory(
            "custody");
        var databasePath = Path.Combine(testDirectory, "inventory.db");

        try
        {
            var connectionString = SqliteDatabaseConfiguration.PrepareConnectionString(
                $"Data Source={databasePath};Pooling=False",
                ApiContentRoot,
                requireProductionFile: true);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite =>
                        sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName))
                .Options;
            var now = DateTimeOffset.UtcNow;
            var category = AssetCategory.Create("CUSTODY", "Custody invariants");
            var inactiveTargetAsset = Asset.Create(
                CreateAssetDetails("DB-ROLLBACK", category.Id),
                now);
            var assignedAsset = Asset.Create(
                CreateAssetDetails("DB-DEACTIVATE", category.Id),
                now);
            var invalidTargetAsset = Asset.Create(
                CreateAssetDetails("DB-XOR", category.Id),
                now);
            var inactivePerson = Person.Create(
                "DB-INACTIVE",
                "Inactive Person",
                PersonType.Other);
            inactivePerson.Update(
                inactivePerson.ExternalId,
                inactivePerson.DisplayName,
                inactivePerson.Type,
                inactivePerson.Email,
                isActive: false,
                inactivePerson.Version);
            var activePerson = Person.Create(
                "DB-ACTIVE",
                "Active Person",
                PersonType.Other);
            var department = Department.Create("DB-DEPT", "Database Department");
            var actor = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "custody-test@example.test",
                NormalizedUserName = "CUSTODY-TEST@EXAMPLE.TEST",
                Email = "custody-test@example.test",
                NormalizedEmail = "CUSTODY-TEST@EXAMPLE.TEST",
                DisplayName = "Custody Test",
                IsEnabled = true,
                CreatedAtUtc = now,
            };

            await using (var dbContext = new InventoryDbContext(options))
            {
                await dbContext.Database.MigrateAsync();
                dbContext.AddRange(
                    category,
                    inactiveTargetAsset,
                    assignedAsset,
                    invalidTargetAsset,
                    inactivePerson,
                    activePerson,
                    department,
                    actor);
                await dbContext.SaveChangesAsync();

                inactiveTargetAsset.Assign(inactiveTargetAsset.Version, now.AddMinutes(1));
                dbContext.AssetAssignments.Add(
                    AssetAssignment.Create(
                        inactiveTargetAsset.Id,
                        inactivePerson.Id,
                        null,
                        inactivePerson.DisplayName,
                        inactivePerson.ExternalId,
                        now.AddMinutes(1),
                        actor.Id,
                        actor.DisplayName,
                        inactiveTargetAsset.Condition,
                        inactiveTargetAsset.LocationId));
                dbContext.AssetEvents.Add(
                    AssetEvent.Create(
                        inactiveTargetAsset.Id,
                        AssetEventType.Assigned,
                        now.AddMinutes(1),
                        actor.Id,
                        actor.DisplayName,
                        AssetEventSource.Api,
                        "This transaction must roll back.",
                        "{}"));

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => dbContext.SaveChangesAsync());
                dbContext.ChangeTracker.Clear();
            }

            await using (var verification = new InventoryDbContext(options))
            {
                var rolledBackAsset = await verification.Assets.SingleAsync(
                    asset => asset.Id == inactiveTargetAsset.Id);
                Assert.Equal(AssetStatus.InStock, rolledBackAsset.Status);
                Assert.False(
                    await verification.AssetAssignments.AnyAsync(
                        assignment => assignment.AssetId == inactiveTargetAsset.Id));
                Assert.False(
                    await verification.AssetEvents.AnyAsync(
                        assetEvent => assetEvent.AssetId == inactiveTargetAsset.Id));

                var trackedAsset = await verification.Assets.SingleAsync(
                    asset => asset.Id == assignedAsset.Id);
                var trackedPerson = await verification.People.SingleAsync(
                    person => person.Id == activePerson.Id);
                trackedAsset.Assign(trackedAsset.Version, now.AddMinutes(2));
                verification.AssetAssignments.Add(
                    AssetAssignment.Create(
                        trackedAsset.Id,
                        trackedPerson.Id,
                        null,
                        trackedPerson.DisplayName,
                        trackedPerson.ExternalId,
                        now.AddMinutes(2),
                        actor.Id,
                        actor.DisplayName,
                        trackedAsset.Condition,
                        trackedAsset.LocationId));
                await verification.SaveChangesAsync();

                trackedPerson.Update(
                    trackedPerson.ExternalId,
                    trackedPerson.DisplayName,
                    trackedPerson.Type,
                    trackedPerson.Email,
                    isActive: false,
                    trackedPerson.Version);
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => verification.SaveChangesAsync());
                verification.ChangeTracker.Clear();

                var xorException = await Assert.ThrowsAsync<SqliteException>(
                    () => verification.Database.ExecuteSqlRawAsync(
                        """
                        INSERT INTO "AssetAssignments"
                            ("Id", "AssetId", "PersonId", "DepartmentId",
                             "TargetDisplayName", "AssignedAtUtc", "AssignedByUserId",
                             "AssignedByDisplayName", "AssignedCondition", "Version")
                        VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9});
                        """,
                        Guid.NewGuid(),
                        invalidTargetAsset.Id,
                        activePerson.Id,
                        department.Id,
                        "Invalid dual target",
                        now.UtcDateTime,
                        actor.Id,
                        actor.DisplayName,
                        AssetCondition.Good.ToString(),
                        Guid.NewGuid()));
                Assert.Contains(
                    "CK_AssetAssignments_ExactlyOneTarget",
                    xorException.Message,
                    StringComparison.Ordinal);
            }

            await using (var finalVerification = new InventoryDbContext(options))
            {
                Assert.True(
                    (await finalVerification.People.SingleAsync(
                        person => person.Id == activePerson.Id)).IsActive);
                Assert.Single(
                    await finalVerification.AssetAssignments
                        .Where(assignment => assignment.AssetId == assignedAsset.Id)
                        .ToArrayAsync());
            }
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                foreach (var file in Directory.GetFiles(testDirectory))
                {
                    File.Delete(file);
                }

                Directory.Delete(testDirectory);
            }
        }
    }

    [Fact]
    public void ProductionSqliteConfigurationRejectsMemoryAndRelativeDatabases()
    {
        var basePath = Path.GetTempPath();

        Assert.Throws<InvalidOperationException>(
            () => SqliteDatabaseConfiguration.PrepareConnectionString(
                "Data Source=:memory:",
                basePath,
                requireProductionFile: true));
        Assert.Throws<InvalidOperationException>(
            () => SqliteDatabaseConfiguration.PrepareConnectionString(
                "Data Source=data/inventory.db",
                basePath,
                requireProductionFile: true));
    }

    private static async Task<string> ReadPragmaAsync(
        InventoryDbContext dbContext,
        string name)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        var value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"PRAGMA {name} returned no value.");
    }

    private static string NewTestDirectory(string name) =>
        Path.Combine(
            RepositoryRoot,
            "artifacts",
            "sqlite-migration-tests",
            name,
            Guid.NewGuid().ToString("N"));

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(
            Path.GetDirectoryName(
                typeof(SqliteMigrationTests).Assembly.Location)!);
        while (candidate is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        candidate.FullName,
                        "Inventory.Rewrite.slnx")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the Inventory rewrite repository root.");
    }

    private static AssetDetails CreateAssetDetails(string assetTag, Guid categoryId) =>
        new(
            assetTag,
            categoryId,
            null,
            null,
            null,
            AssetStatus.InStock,
            AssetCondition.Good,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static async Task<string> ReadSchemaSqlAsync(
        InventoryDbContext dbContext,
        string type,
        string name)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = $type AND name = $name;";
        var typeParameter = command.CreateParameter();
        typeParameter.ParameterName = "$type";
        typeParameter.Value = type;
        command.Parameters.Add(typeParameter);
        var nameParameter = command.CreateParameter();
        nameParameter.ParameterName = "$name";
        nameParameter.Value = name;
        command.Parameters.Add(nameParameter);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                $"SQLite schema entry '{type}:{name}' was not found.");
    }
}
