using Inventory.Application.Abstractions;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Domain.Agents;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Inventory.Domain.Locations;
using Inventory.Domain.People;
using Inventory.Domain.Qr;
using Inventory.Domain.Stocktakes;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class BackupRestoreRehearsalTests
{
    [Fact]
    public async Task OnlineBackupAndIsolatedRestoreRehearsalVerifyAllCriticalStores()
    {
        using var factory = new FileDatabaseFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.MigrateAsync();

        var roleSeeder = services.GetRequiredService<IRolePermissionSeeder>();
        await roleSeeder.SeedAsync(CancellationToken.None);
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = FileDatabaseFactory.RehearsalEmail,
            Email = FileDatabaseFactory.RehearsalEmail,
            NormalizedUserName =
                FileDatabaseFactory.RehearsalEmail.ToUpperInvariant(),
            NormalizedEmail =
                FileDatabaseFactory.RehearsalEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = "Restore Rehearsal Administrator",
            IsEnabled = true,
            MustChangePassword = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        Assert.True(
            (await userManager.CreateAsync(
                user,
                FileDatabaseFactory.RehearsalPassword)).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(
                user,
                RoleNames.Administrator)).Succeeded);
        Assert.True(
            (await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.False(
            string.IsNullOrWhiteSpace(
                await userManager.GetAuthenticatorKeyAsync(user)));
        Assert.True(
            (await userManager.SetTwoFactorEnabledAsync(
                user,
                enabled: true)).Succeeded);
        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user,
                number: 2);
        Assert.NotNull(recoveryCodes);
        Assert.Equal(2, recoveryCodes.Count());

        var fixtureTime =
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var category = AssetCategory.Create("LAPTOP", "Laptop");
        var location = Location.Create(
            "STORE-01",
            "ICT Store",
            "Store room");
        var person = Person.Create(
            "RESTORE-LEAVER-001",
            "Restore Rehearsal Leaver",
            PersonType.Staff,
            "restore-leaver@inventory.test");
        var asset = Asset.Create(
            new AssetDetails(
                AssetTag: "REHEARSAL-001",
                CategoryId: category.Id,
                Manufacturer: "Contoso",
                Model: "RestoreBook",
                SerialNumber: "RESTORE-SERIAL-001",
                Status: AssetStatus.InStock,
                Condition: AssetCondition.Good,
                LocationId: location.Id,
                PurchaseDate: new DateOnly(2026, 1, 1),
                PurchaseCost: 1000m,
                Currency: "GBP",
                Supplier: "Restore Supplier",
                Notes: "Representative restore-rehearsal asset.",
                LegacyId: "LEGACY-RESTORE-001",
                DeviceName: "REHEARSAL-LAPTOP"),
            fixtureTime);
        var tokenProtector = services.GetRequiredService<IQrTokenProtector>();
        var token = tokenProtector.Create();
        var qrIdentifier = QrIdentifier.Issue(
            asset.Id,
            token.TokenHash,
            token.ProtectedToken,
            tokenVersion: 1,
            fixtureTime,
            user.Id,
            user.DisplayName);
        var assetEvent = AssetEvent.Create(
            asset.Id,
            AssetEventType.QrIssued,
            fixtureTime,
            user.Id,
            user.DisplayName,
            AssetEventSource.Api,
            "Representative QR issued for restore rehearsal.",
            "{}",
            "restore-rehearsal-test");
        var audit = AuditLog.Create(
            user.Id,
            user.DisplayName,
            "restore.rehearsal.fixture",
            "asset",
            asset.Id.ToString(),
            fixtureTime,
            "succeeded",
            correlationId: "restore-rehearsal-test");
        var enrollmentProfile = AgentEnrollmentProfile.Create(
            Guid.NewGuid(),
            "Restore rehearsal deployment",
            Convert.ToBase64String(new byte[32]),
            fixtureTime.AddHours(1),
            maxEnrollments: 5,
            requireTpm: true,
            allowedHostnameSuffix: "school.example",
            user.Id,
            fixtureTime.AddMinutes(-15));
        const string agentHostname = "restore-agent.school.example";
        enrollmentProfile.Consume(
            agentHostname,
            AgentKeyProtection.Tpm,
            fixtureTime.AddMinutes(-10));
        var agent = ManagedAgent.Enroll(
            enrollmentProfile.Id,
            "restore-rehearsal-device",
            "restore-rehearsal-public-key",
            new string('a', 64),
            AgentKeyProtection.Tpm,
            agentHostname,
            "RESTORE-AGENT-SERIAL",
            "46de1023-9002-4a39-9e4a-337cb43d82d6",
            "Contoso",
            "Restore Agent",
            fixtureTime.AddMinutes(-10));
        var snapshotId = Guid.NewGuid();
        agent.RecordCheckIn(
            sequence: 1,
            snapshotId,
            fixtureTime.AddMinutes(-2),
            fixtureTime,
            agentVersion: "1.0.0",
            agentHostname,
            serialNumber: "RESTORE-AGENT-SERIAL",
            biosUuid: "46de1023-9002-4a39-9e4a-337cb43d82d6",
            manufacturer: "Contoso",
            model: "Restore Agent",
            seenIpAddress: "192.0.2.10",
            burstWindow: TimeSpan.FromMinutes(15),
            burstLimit: 144,
            quotaWindow: TimeSpan.FromHours(24),
            quotaLimit: 192);
        var snapshot = ManagedAgentSnapshot.Create(
            agent.Id,
            snapshotId,
            sequence: 1,
            fixtureTime.AddMinutes(-2),
            fixtureTime,
            Convert.ToBase64String(new byte[32]),
            Convert.ToBase64String(new byte[32]),
            "{\"fixture\":true}",
            agentHostname,
            "RESTORE-AGENT-SERIAL",
            "46de1023-9002-4a39-9e4a-337cb43d82d6",
            "1.0.0");
        var keyRecovery = AgentKeyRecoveryAuthorization.Create(
            Guid.NewGuid(),
            agent.Id,
            Convert.ToBase64String(new byte[32]),
            "Representative restore-rehearsal recovery authorization.",
            requireTpm: true,
            agent.Version,
            user.Id,
            user.DisplayName,
            fixtureTime,
            fixtureTime.AddMinutes(30));
        var requestNonce = AgentRequestNonce.Create(
            AgentRequestNonceScope.CheckIn,
            agent.Id,
            new string('A', 44),
            fixtureTime,
            fixtureTime.AddMinutes(10));
        var assignment = AssetAssignment.Create(
            asset.Id,
            person.Id,
            departmentId: null,
            person.DisplayName,
            person.ExternalId,
            fixtureTime.AddDays(-7),
            user.Id,
            user.DisplayName,
            AssetCondition.Good,
            location.Id,
            "Representative temporary loan.",
            expectedReturnOn: new DateOnly(2026, 7, 25));
        asset.Assign(asset.Version, fixtureTime.AddDays(-7));
        var bulkChangeJob = AssetBulkChangeJob.Create(
            AssetBulkChangeOperationKind.AddNote,
            """{"note":"Checked during restore rehearsal."}""",
            $$"""[{"assetId":"{{asset.Id}}","version":"{{asset.Version}}"}]""",
            "Representative controlled bulk-change preview.",
            new string('b', 64),
            assetCount: 1,
            user.Id,
            user.DisplayName,
            fixtureTime.AddMinutes(-5),
            fixtureTime.AddMinutes(25));
        var bundle = AssetBundle.Create(
            "REHEARSAL-KIT-001",
            "Restore Rehearsal Laptop Kit",
            "Representative bundle retained through backup and restore.",
            [
                new AssetBundleMemberSpec(
                    asset.Id,
                    AssetBundleMemberRole.Laptop,
                    OtherRole: null),
            ],
            fixtureTime.AddMinutes(-4));
        var stocktake = StocktakeSession.Start(
            "Restore rehearsal stocktake",
            StocktakeScope.All,
            scopeLocationId: null,
            scopeCategoryId: null,
            fixtureTime.AddMinutes(-3),
            user.Id,
            user.DisplayName);
        var stocktakeExpected = StocktakeExpectedAsset.Capture(
            stocktake.Id,
            asset.Id,
            asset.AssetTag,
            asset.DeviceName,
            category.Id,
            category.Code,
            category.Name,
            asset.Status,
            asset.Condition,
            location.Id,
            location.Code,
            location.Name);
        var stocktakeObservation = StocktakeObservation.Record(
            stocktake.Id,
            asset.Id,
            asset.AssetTag,
            asset.DeviceName,
            category.Id,
            category.Code,
            category.Name,
            location.Id,
            location.Code,
            location.Name,
            asset.Condition,
            "Representative stocktake observation.",
            fixtureTime.AddMinutes(-2),
            user.Id,
            user.DisplayName);
        var leaverCase = LeaverCase.Create(
            person.Id,
            new DateOnly(2026, 8, 31),
            "Representative open leaver custody case.",
            fixtureTime.AddMinutes(-1),
            user.Id,
            user.DisplayName);
        byte[] attachmentContent =
        [
            0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37,
            0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A,
        ];
        var attachmentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(attachmentContent));
        var attachment = AssetAttachment.Create(
            asset.Id,
            AssetAttachmentCategory.Invoice,
            "restore-rehearsal-invoice.pdf",
            "application/pdf",
            attachmentContent,
            attachmentHash,
            fixtureTime,
            user.Id,
            user.DisplayName);
        var savedSearch = SavedAssetSearch.Create(
            user.Id,
            "Restore rehearsal assets",
            SavedAssetSearch.CurrentSchemaVersion,
            """{"status":["assigned"],"search":"REHEARSAL"}""",
            fixtureTime);
        savedSearch.SetDefault(
            isDefault: true,
            savedSearch.Version,
            fixtureTime.AddMinutes(1));

        dbContext.AssetCategories.Add(category);
        dbContext.Locations.Add(location);
        dbContext.People.Add(person);
        dbContext.Assets.Add(asset);
        dbContext.AssetAssignments.Add(assignment);
        dbContext.QrIdentifiers.Add(qrIdentifier);
        dbContext.AssetEvents.Add(assetEvent);
        dbContext.AuditLogs.Add(audit);
        dbContext.AgentEnrollmentProfiles.Add(enrollmentProfile);
        dbContext.ManagedAgents.Add(agent);
        dbContext.ManagedAgentSnapshots.Add(snapshot);
        dbContext.AgentKeyRecoveryAuthorizations.Add(keyRecovery);
        dbContext.AgentRequestNonces.Add(requestNonce);
        dbContext.AssetBulkChangeJobs.Add(bulkChangeJob);
        dbContext.AssetBundles.Add(bundle);
        dbContext.StocktakeSessions.Add(stocktake);
        dbContext.StocktakeExpectedAssets.Add(stocktakeExpected);
        dbContext.StocktakeObservations.Add(stocktakeObservation);
        dbContext.AssetAttachments.Add(attachment);
        dbContext.SavedAssetSearches.Add(savedSearch);
        await dbContext.SaveChangesAsync();
        dbContext.LeaverCases.Add(leaverCase);
        await dbContext.SaveChangesAsync();

        var connection = dbContext.Database.GetDbConnection();
        var openedConnection =
            connection.State != System.Data.ConnectionState.Open;
        if (openedConnection)
        {
            await connection.OpenAsync();
        }

        string? journalMode;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            journalMode = Convert.ToString(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }

        Assert.Equal("wal", journalMode, ignoreCase: true);

        var auditCountAtSnapshot = await dbContext.AuditLogs.LongCountAsync();
        var backupRestore = Assert.IsType<BackupRestoreService>(
            services.GetRequiredService<IBackupRestoreService>());
        backupRestore.AfterSnapshotForTesting = async cancellationToken =>
        {
            dbContext.AuditLogs.Add(AuditLog.Create(
                user.Id,
                user.DisplayName,
                "restore.rehearsal.post_snapshot_write",
                "asset",
                asset.Id.ToString(),
                fixtureTime.AddMinutes(2),
                "succeeded"));
            await dbContext.SaveChangesAsync(cancellationToken);
        };
        var backup = await backupRestore.CreateBackupAsync(
            CancellationToken.None);
        Assert.Equal(auditCountAtSnapshot, backup.Manifest.RowCounts["auditLogs"]);
        Assert.Equal(
            auditCountAtSnapshot + 1,
            await dbContext.AuditLogs.LongCountAsync());
        Assert.True(File.Exists(Path.Combine(
            backup.BackupDirectory,
            "manifest.json")));
        Assert.Contains(
            backup.Manifest.Files,
            file => file.RelativePath == "inventory.db");
        Assert.Contains(
            backup.Manifest.Files,
            file => file.RelativePath.StartsWith(
                "keys/",
                StringComparison.Ordinal));
        Assert.Equal(1, backup.Manifest.RowCounts["assets"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assetAttachments"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assetBulkChangeJobs"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assetBundleMembers"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assetBundles"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assetTagPolicies"]);
        Assert.Equal(1, backup.Manifest.RowCounts["assignments"]);
        Assert.True(backup.Manifest.RowCounts.ContainsKey("assetImportJobs"));
        Assert.True(backup.Manifest.RowCounts.ContainsKey("referenceDataImportJobs"));
        Assert.True(backup.Manifest.RowCounts.ContainsKey("legacyReconciliationRuns"));
        Assert.True(backup.Manifest.RowCounts.ContainsKey("legacyReconciliationSignOffs"));
        Assert.Equal(1, backup.Manifest.RowCounts["leaverCases"]);
        Assert.Equal(1, backup.Manifest.RowCounts["savedAssetSearches"]);
        Assert.True(backup.Manifest.RowCounts.ContainsKey("schoolProfiles"));
        Assert.Equal(1, backup.Manifest.RowCounts["stocktakeExpectedAssets"]);
        Assert.Equal(1, backup.Manifest.RowCounts["stocktakeObservations"]);
        Assert.Equal(1, backup.Manifest.RowCounts["stocktakeSessions"]);
        Assert.True(backup.Manifest.RowCounts["roles"] > 0);
        Assert.True(backup.Manifest.RowCounts["userRoles"] > 0);
        Assert.True(backup.Manifest.RowCounts["roleClaims"] > 0);
        Assert.True(backup.Manifest.RowCounts.ContainsKey("userClaims"));
        Assert.True(backup.Manifest.RowCounts.ContainsKey("userLogins"));
        Assert.True(backup.Manifest.RowCounts["userTokens"] > 0);
        Assert.Equal(1, backup.Manifest.RowCounts["agentEnrollmentProfiles"]);
        Assert.Equal(
            1,
            backup.Manifest.RowCounts["agentKeyRecoveryAuthorizations"]);
        Assert.Equal(1, backup.Manifest.RowCounts["managedAgents"]);
        Assert.Equal(1, backup.Manifest.RowCounts["managedAgentSnapshots"]);
        Assert.Equal(1, backup.Manifest.RowCounts["agentRequestNonces"]);

        var rehearsal = await backupRestore.RehearseLatestRestoreAsync(
            CancellationToken.None);
        Assert.Equal("passed", rehearsal.Report.Status);
        Assert.True(File.Exists(rehearsal.ReportPath));
        Assert.All(
            rehearsal.Report.Checks,
            check => Assert.True(check.Passed, check.Detail));
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "sqlite_integrity");
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "login");
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "identity_authorization_and_mfa");
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "qr_resolution");
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "asset_history");
        Assert.Contains(
            rehearsal.Report.Checks,
            check => check.Name == "audit_sample");
        var attachmentEvidence = Assert.Single(
            rehearsal.Report.Checks,
            check => check.Name == "attachment_content_hashes");
        Assert.Contains("1 restored attachment", attachmentEvidence.Detail);
        Assert.NotEmpty(rehearsal.Report.RollbackProcedure);
        Assert.Empty(
            Directory.EnumerateDirectories(
                factory.BackupPath,
                ".rehearsal-*",
                SearchOption.TopDirectoryOnly));
    }

    private sealed class FileDatabaseFactory
        : WebApplicationFactory<Program>
    {
        public const string RehearsalEmail =
            "restore-rehearsal@inventory.test";
        public const string RehearsalPassword =
            "Restore-Rehearsal-Password!42";

        private static readonly string RepositoryRoot =
            FindRepositoryRoot();

        private readonly string _root = Path.Combine(
            RepositoryRoot,
            "artifacts",
            "backup-restore-tests",
            Guid.NewGuid().ToString("N"));

        public string BackupPath => Path.Combine(_root, "backups");

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            var databasePath = Path.Combine(
                _root,
                "data",
                "inventory.db");
            var keyPath = Path.Combine(_root, "keys");
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:InventoryDatabase",
                $"Data Source={databasePath};Foreign Keys=True");
            builder.UseSetting(
                "Security:DataProtectionKeysPath",
                keyPath);
            builder.UseSetting(
                "Security:ProtectKeysWithDpapi",
                bool.TrueString);
            builder.UseSetting(
                "Storage:BackupPath",
                BackupPath);
            builder.UseSetting(
                "Storage:MinimumFreeSpaceBytes",
                (64L * 1024 * 1024).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting(
                "Storage:RehearsalLoginEmail",
                RehearsalEmail);
            builder.UseSetting(
                "Storage:RehearsalLoginPassword",
                RehearsalPassword);
            builder.UseSetting(
                "Storage:RehearsalRequiresActiveQr",
                bool.TrueString);
            builder.UseSetting(
                "Mfa:EnforceRequiredRoles",
                bool.FalseString);
            builder.UseSetting(
                "Identity:ReconcileRolePresetsOnStartup",
                bool.FalseString);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || !Directory.Exists(_root))
            {
                return;
            }

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string FindRepositoryRoot()
        {
            var candidate = new DirectoryInfo(
                Path.GetDirectoryName(
                    typeof(BackupRestoreRehearsalTests)
                        .Assembly.Location)!);
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
    }
}
