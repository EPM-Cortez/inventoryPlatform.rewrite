using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class AgentApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AgentApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RequireTpmProfileFailsClosedWithoutVerifiedAttestation()
    {
        using var administrator = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var response = await administrator.PostAsJsonAsync(
            "/api/v1/agents/enrollment-profiles",
            new CreateAgentEnrollmentProfileRequest
            {
                Name = "Verified hardware only",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                MaxEnrollments = 1,
                RequireTpm = true,
            },
            ApiTestClient.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "agent_tpm_attestation_unavailable",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedAgentLifecycleLinksExactAssetAndRejectsRevokedKey()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var biosUuid = Guid.NewGuid();
        var serialNumber = $"AGENT-SERIAL-{suffix}";
        var asset = await CreateAssetAsync(
            administrator,
            suffix,
            serialNumber,
            biosUuid);
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Agent deployment {suffix}",
            maxEnrollments: 1);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = new AgentIdentityRequest
        {
            Hostname = $"pc-{suffix}.school.example",
            SerialNumber = serialNumber,
            BiosUuid = biosUuid.ToString(),
            Manufacturer = "Dell",
            Model = "Latitude",
        };
        var enrollment = CreateEnrollmentRequest(
            profile.EnrollmentToken,
            $"win-{suffix}",
            identity,
            key);
        var enrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            enrollment,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, enrollmentResponse.StatusCode);
        var enrolled = await enrollmentResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);
        Assert.Equal(1, enrolled.NextSequence);

        var replayedEnrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            enrollment,
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayedEnrollmentResponse.StatusCode);

        var retriedEnrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"win-{suffix}",
                identity,
                key),
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Created,
            retriedEnrollmentResponse.StatusCode);

        using var secondKey = ECDsa.Create(
            ECCurve.NamedCurves.nistP256);
        var exhaustedResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"win-second-{suffix}",
                WithHostname(
                    identity,
                    $"pc-second-{suffix}.school.example"),
                secondKey),
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            exhaustedResponse.StatusCode);

        var snapshot = CreateSnapshot(
            serialNumber,
            biosUuid,
            identity.Hostname);
        var checkIn = CreateCheckInRequest(
            enrolled.AgentId,
            enrolled.DeviceId,
            enrolled.NextSequence,
            snapshot,
            key);
        var checkInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            checkIn,
            ApiTestClient.JsonOptions);
        checkInResponse.EnsureSuccessStatusCode();
        var accepted = await checkInResponse.Content
            .ReadFromJsonAsync<AgentCheckInResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(accepted);
        Assert.True(accepted.Accepted);
        Assert.False(accepted.Duplicate);
        Assert.Equal(asset.Id, accepted.LinkedAssetId);
        Assert.Equal(
            enrolled.CheckInIntervalSeconds,
            accepted.CheckInIntervalSeconds);

        var replayedCheckInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            checkIn,
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayedCheckInResponse.StatusCode);

        var duplicateResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                enrolled.NextSequence,
                snapshot,
                key),
            ApiTestClient.JsonOptions);
        duplicateResponse.EnsureSuccessStatusCode();
        var duplicate = await duplicateResponse.Content
            .ReadFromJsonAsync<AgentCheckInResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(
            enrolled.CheckInIntervalSeconds,
            duplicate.CheckInIntervalSeconds);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository =
                scope.ServiceProvider.GetRequiredService<IAgentRepository>();
            var persisted = await repository.GetByAssetIdAsync(
                asset.Id,
                CancellationToken.None);
            Assert.NotNull(persisted);
        }

        var deviceInventory = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/assets/{asset.Id}/device-inventory",
            ApiTestClient.JsonOptions);
        Assert.NotNull(deviceInventory);
        Assert.Equal(asset.Id, deviceInventory.Agent.LinkedAssetId);
        Assert.Equal(AgentKeyProtectionContract.Tpm, deviceInventory.Agent.KeyProtection);
        Assert.False(deviceInventory.Agent.HardwareAttestationVerified);
        Assert.Equal(
            snapshot.SnapshotId,
            deviceInventory.LatestSnapshot?.SnapshotId);
        Assert.Equal(
            snapshot.OperatingSystem.Build,
            deviceInventory.LatestSnapshot?.OperatingSystem.Build);

        var unlinkResponse = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/unlink",
            new UnlinkManagedAgentRequest
            {
                Reason = "Verify that an explicit unlink remains in review",
                Version = deviceInventory.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        unlinkResponse.EnsureSuccessStatusCode();
        var unlinked = await unlinkResponse.Content.ReadFromJsonAsync<
            ManagedAgentDetailResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(unlinked);
        Assert.Null(unlinked.Agent.LinkedAssetId);
        Assert.Equal(
            ManagedAgentMatchStateContract.Conflict,
            unlinked.Agent.MatchState);

        var invalidSignature = CreateCheckInRequest(
            enrolled.AgentId,
            enrolled.DeviceId,
            2,
            CreateSnapshot(
                serialNumber,
                biosUuid,
                identity.Hostname),
            key);
        invalidSignature = CopyWithSignature(
            invalidSignature,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        var invalidSignatureResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            invalidSignature,
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            invalidSignatureResponse.StatusCode);

        var checkInAfterUnlinkResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                2,
                CreateSnapshot(
                    serialNumber,
                    biosUuid,
                    identity.Hostname),
                key),
            ApiTestClient.JsonOptions);
        checkInAfterUnlinkResponse.EnsureSuccessStatusCode();
        var acceptedAfterUnlink = await checkInAfterUnlinkResponse.Content
            .ReadFromJsonAsync<AgentCheckInResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(acceptedAfterUnlink);
        Assert.Null(acceptedAfterUnlink.LinkedAssetId);

        var current = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        var revoke = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/revoke",
            new RevokeManagedAgentRequest
            {
                Reason = "Integration test revocation",
                Version = current.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        revoke.EnsureSuccessStatusCode();

        var revokedCheckIn = CreateCheckInRequest(
            enrolled.AgentId,
            enrolled.DeviceId,
            3,
            CreateSnapshot(
                serialNumber,
                biosUuid,
                identity.Hostname),
            key);
        var revokedResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            revokedCheckIn,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);

        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.enrolled"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.asset.auto_linked"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.asset.unlinked"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.revoked"));
    }

    [Fact]
    public async Task TechnicianCanReadAgentsButCannotMintEnrollmentToken()
    {
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        var listResponse = await technician.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var createResponse = await technician.PostAsJsonAsync(
            "/api/v1/agents/enrollment-profiles",
            new CreateAgentEnrollmentProfileRequest
            {
                Name = "Forbidden technician profile",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                MaxEnrollments = 1,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task ContradictoryOrAlreadyOwnedIdentifiersRequireManualReview()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var linkedBios = Guid.NewGuid();
        var linkedSerial = $"OWNED-SERIAL-{suffix}";
        var unlinkedSerial = $"FREE-SERIAL-{suffix}";
        var linkedAsset = await CreateAssetAsync(
            administrator,
            $"owned-{suffix}",
            linkedSerial,
            linkedBios);
        var unlinkedAsset = await CreateAssetAsync(
            administrator,
            $"free-{suffix}",
            unlinkedSerial,
            Guid.NewGuid());
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Conflict matching {suffix}",
            maxEnrollments: 2);

        using var firstKey =
            ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var firstIdentity = new AgentIdentityRequest
        {
            Hostname = $"owned-{suffix}.school.example",
            SerialNumber = linkedSerial,
            BiosUuid = linkedBios.ToString(),
            Manufacturer = "Example",
            Model = "Owned",
        };
        var firstEnrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"owned-device-{suffix}",
                firstIdentity,
                firstKey),
            ApiTestClient.JsonOptions);
        firstEnrollmentResponse.EnsureSuccessStatusCode();
        var firstEnrollment = await firstEnrollmentResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(firstEnrollment);
        var firstCheckInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                firstEnrollment.AgentId,
                firstEnrollment.DeviceId,
                1,
                CreateSnapshot(
                    linkedSerial,
                    linkedBios,
                    firstIdentity.Hostname),
                firstKey),
            ApiTestClient.JsonOptions);
        firstCheckInResponse.EnsureSuccessStatusCode();
        var firstCheckIn = await firstCheckInResponse.Content
            .ReadFromJsonAsync<AgentCheckInResponse>(
                ApiTestClient.JsonOptions);
        Assert.Equal(linkedAsset.Id, firstCheckIn?.LinkedAssetId);

        using var secondKey =
            ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var secondIdentity = new AgentIdentityRequest
        {
            Hostname = $"conflict-{suffix}.school.example",
            SerialNumber = unlinkedSerial,
            BiosUuid = linkedBios.ToString(),
            Manufacturer = "Example",
            Model = "Conflict",
        };
        var secondEnrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"conflict-device-{suffix}",
                secondIdentity,
                secondKey),
            ApiTestClient.JsonOptions);
        secondEnrollmentResponse.EnsureSuccessStatusCode();
        var secondEnrollment = await secondEnrollmentResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(secondEnrollment);
        var secondCheckInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                secondEnrollment.AgentId,
                secondEnrollment.DeviceId,
                1,
                CreateSnapshot(
                    unlinkedSerial,
                    linkedBios,
                    secondIdentity.Hostname),
                secondKey),
            ApiTestClient.JsonOptions);
        secondCheckInResponse.EnsureSuccessStatusCode();
        var secondCheckIn = await secondCheckInResponse.Content
            .ReadFromJsonAsync<AgentCheckInResponse>(
                ApiTestClient.JsonOptions);
        Assert.Null(secondCheckIn?.LinkedAssetId);

        var conflict = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{secondEnrollment.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(conflict);
        Assert.Equal(
            ManagedAgentMatchStateContract.Conflict,
            conflict.Agent.MatchState);
        Assert.Contains(
            "Multiple assets",
            conflict.Agent.MatchReason,
            StringComparison.Ordinal);
        using var unlinkedInventory = await administrator.GetAsync(
            $"/api/v1/assets/{unlinkedAsset.Id}/device-inventory");
        Assert.Equal(HttpStatusCode.NotFound, unlinkedInventory.StatusCode);
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                secondEnrollment.AgentId,
                "agent.asset.match_conflict"));
    }

    [Fact]
    public async Task ManagementQueriesAndReviewLifecycleAreServerSideAndAudited()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Fleet management {suffix}",
            maxEnrollments: 3);
        var online = await EnrollUnmatchedAgentAsync(
            agentClient,
            profile.EnrollmentToken,
            $"fleet-online-{suffix}",
            $"online-{suffix}.school.example");
        var stale = await EnrollUnmatchedAgentAsync(
            agentClient,
            profile.EnrollmentToken,
            $"fleet-stale-{suffix}",
            $"stale-{suffix}.school.example");
        var offline = await EnrollUnmatchedAgentAsync(
            agentClient,
            profile.EnrollmentToken,
            $"fleet-offline-{suffix}",
            $"offline-{suffix}.school.example");

        var now = DateTimeOffset.UtcNow;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await dbContext.ManagedAgents
                .Where(agent => agent.Id == online.AgentId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        agent => agent.LastCheckInAtUtc,
                        now.AddMinutes(-1)));
            await dbContext.ManagedAgents
                .Where(agent => agent.Id == stale.AgentId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        agent => agent.LastCheckInAtUtc,
                        now.AddHours(-24)));
            await dbContext.ManagedAgents
                .Where(agent => agent.Id == offline.AgentId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        agent => agent.LastCheckInAtUtc,
                        now.AddHours(-96)));
        }

        var onlinePage = await administrator.GetFromJsonAsync<
            PagedResponse<ManagedAgentListItemResponse>>(
            $"/api/v1/agents?q={suffix}&connectivity=online&pageSize=10",
            ApiTestClient.JsonOptions);
        Assert.NotNull(onlinePage);
        Assert.Equal(1, onlinePage.TotalCount);
        Assert.Equal(online.AgentId, Assert.Single(onlinePage.Items).Id);
        Assert.Equal(
            ManagedAgentConnectivityStatusContract.Online,
            onlinePage.Items[0].ConnectivityStatus);

        var stalePage = await administrator.GetFromJsonAsync<
            PagedResponse<ManagedAgentListItemResponse>>(
            $"/api/v1/agents?q={suffix}&connectivity=stale&pageSize=10",
            ApiTestClient.JsonOptions);
        Assert.NotNull(stalePage);
        Assert.Equal(stale.AgentId, Assert.Single(stalePage.Items).Id);

        var offlinePage = await administrator.GetFromJsonAsync<
            PagedResponse<ManagedAgentListItemResponse>>(
            $"/api/v1/agents?q={suffix}&connectivity=offline&pageSize=10",
            ApiTestClient.JsonOptions);
        Assert.NotNull(offlinePage);
        Assert.Equal(offline.AgentId, Assert.Single(offlinePage.Items).Id);

        var unmatchedPage = await administrator.GetFromJsonAsync<
            PagedResponse<ManagedAgentListItemResponse>>(
            $"/api/v1/agents?q={suffix}&matchState=unmatched&pageSize=10",
            ApiTestClient.JsonOptions);
        Assert.NotNull(unmatchedPage);
        Assert.Equal(3, unmatchedPage.TotalCount);

        using var numericEnumQuery = await administrator.GetAsync(
            $"/api/v1/agents?q={suffix}&matchState=1&pageSize=10");
        Assert.Equal(HttpStatusCode.BadRequest, numericEnumQuery.StatusCode);

        using var detailResponse = await administrator.GetAsync(
            $"/api/v1/agents/{online.AgentId}");
        detailResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            "no-store",
            detailResponse.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        var current = await detailResponse.Content.ReadFromJsonAsync<
            ManagedAgentDetailResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        Assert.Equal(profile.Profile.Id, current.Agent.EnrollmentProfileId);
        Assert.Equal(profile.Profile.Name, current.Agent.EnrollmentProfileName);

        using var forbiddenDismiss = await technician.PostAsJsonAsync(
            $"/api/v1/agents/{online.AgentId}/dismiss",
            new DismissManagedAgentRequest
            {
                Reason = "Technician must not manage trust records",
                Version = current.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDismiss.StatusCode);

        var dismissed = await PostAgentActionAsync<
            DismissManagedAgentRequest>(
            administrator,
            online.AgentId,
            "dismiss",
            new DismissManagedAgentRequest
            {
                Reason = "Known disposable lab image",
                Version = current.Agent.Version,
            });
        Assert.Equal(
            ManagedAgentMatchStateContract.Dismissed,
            dismissed.Agent.MatchState);

        var restored = await PostAgentActionAsync<
            RestoreManagedAgentReviewRequest>(
            administrator,
            online.AgentId,
            "restore-review",
            new RestoreManagedAgentReviewRequest
            {
                Reason = "Device is now ready for asset review",
                Version = dismissed.Agent.Version,
            });
        Assert.Equal(
            ManagedAgentMatchStateContract.Unmatched,
            restored.Agent.MatchState);

        var linkedAsset = await CreateAssetAsync(
            administrator,
            $"link-{suffix}",
            $"LINK-SERIAL-{suffix}",
            Guid.NewGuid());
        var linkResponse = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{online.AgentId}/link",
            new LinkManagedAgentRequest
            {
                AssetId = linkedAsset.Id,
                Reason = "Administrator confirmed the asset",
                Version = restored.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        linkResponse.EnsureSuccessStatusCode();
        var linked = await linkResponse.Content.ReadFromJsonAsync<
            ManagedAgentDetailResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(linked);
        Assert.Equal(linkedAsset.Id, linked.Agent.LinkedAssetId);

        var unlinked = await PostAgentActionAsync<
            UnlinkManagedAgentRequest>(
            administrator,
            online.AgentId,
            "unlink",
            new UnlinkManagedAgentRequest
            {
                Reason = "The selected asset was incorrect",
                Version = linked.Agent.Version,
            });
        Assert.Null(unlinked.Agent.LinkedAssetId);
        Assert.Equal(
            ManagedAgentMatchStateContract.Conflict,
            unlinked.Agent.MatchState);

        var summary = await administrator.GetFromJsonAsync<
            ManagedAgentFleetSummaryResponse>(
            "/api/v1/agents/summary",
            ApiTestClient.JsonOptions);
        Assert.NotNull(summary);
        Assert.True(summary.Total >= 3);
        Assert.True(summary.Online >= 1);

        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                online.AgentId,
                "agent.match.dismissed"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                online.AgentId,
                "agent.match.review_restored"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                online.AgentId,
                "agent.asset.unlinked"));
    }

    [Fact]
    public async Task CreateAndLinkAssetRollsBackAssetLinkEventAndAuditOnLateFailure()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Atomic create {suffix}",
            maxEnrollments: 1);
        var enrolled = await EnrollUnmatchedAgentAsync(
            agentClient,
            profile.EnrollmentToken,
            $"atomic-{suffix}",
            $"atomic-{suffix}.school.example");
        var category = await CreateCategoryAsync(
            administrator,
            $"ATOMIC-{suffix}",
            $"Atomic category {suffix}");
        var assetTag = $"ATOMIC-ASSET-{suffix}";
        var forbiddenAssetTag = $"FORBIDDEN-ASSET-{suffix}";
        var triggerName = $"TR_AgentLateFailure_{suffix}";

        var beforeForbiddenAttempt = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(beforeForbiddenAttempt);
        using var forbidden = await technician.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/create-and-link-asset",
            new CreateAndLinkManagedAgentAssetRequest
            {
                AssetTag = forbiddenAssetTag,
                DeviceName = $"forbidden-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
                Reason = "Technician must not cross the agent-management boundary",
                Version = beforeForbiddenAttempt.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenAsset = await administrator.GetAsync(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(forbiddenAssetTag)}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenAsset.StatusCode);
        var afterForbiddenAttempt = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(afterForbiddenAttempt);
        Assert.Null(afterForbiddenAttempt.Agent.LinkedAssetId);
        Assert.Equal(
            beforeForbiddenAttempt.Agent.Version,
            afterForbiddenAttempt.Agent.Version);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var createTriggerSql =
                $"""
                CREATE TRIGGER "{triggerName}"
                BEFORE UPDATE OF "LinkedAssetId" ON "ManagedAgents"
                WHEN NEW."LinkedAssetId" IN (
                    SELECT "Id"
                    FROM "Assets"
                    WHERE "AssetTagNormalized" = '{assetTag.ToUpperInvariant()}'
                )
                BEGIN
                    SELECT RAISE(ABORT, 'forced_late_agent_link_failure');
                END;
                """;
            await dbContext.Database.ExecuteSqlRawAsync(createTriggerSql);
        }

        try
        {
            var current = await administrator.GetFromJsonAsync<
                ManagedAgentDetailResponse>(
                $"/api/v1/agents/{enrolled.AgentId}",
                ApiTestClient.JsonOptions);
            Assert.NotNull(current);
            using var failed = await administrator.PostAsJsonAsync(
                $"/api/v1/agents/{enrolled.AgentId}/create-and-link-asset",
                new CreateAndLinkManagedAgentAssetRequest
                {
                    AssetTag = assetTag,
                    DeviceName = $"atomic-{suffix}",
                    CategoryId = category.Id,
                    Status = AssetStatusContract.InStock,
                    Condition = AssetConditionContract.Good,
                    Reason = "Create the missing asset record",
                    Version = current.Agent.Version,
                },
                ApiTestClient.JsonOptions);
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);

            using var missingAsset = await administrator.GetAsync(
                $"/api/v1/assets/by-tag/{Uri.EscapeDataString(assetTag)}");
            Assert.Equal(HttpStatusCode.NotFound, missingAsset.StatusCode);
            var unchanged = await administrator.GetFromJsonAsync<
                ManagedAgentDetailResponse>(
                $"/api/v1/agents/{enrolled.AgentId}",
                ApiTestClient.JsonOptions);
            Assert.NotNull(unchanged);
            Assert.Null(unchanged.Agent.LinkedAssetId);
            Assert.Equal(current.Agent.Version, unchanged.Agent.Version);
            Assert.Equal(
                0,
                await CountAgentAuditAsync(
                    enrolled.AgentId,
                    "agent.asset.created_and_linked"));
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var dbContext =
                scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var dropTriggerSql =
                $"""DROP TRIGGER IF EXISTS "{triggerName}";""";
            await dbContext.Database.ExecuteSqlRawAsync(dropTriggerSql);
        }

        var retryCurrent = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(retryCurrent);
        using var succeeded = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/create-and-link-asset",
            new CreateAndLinkManagedAgentAssetRequest
            {
                AssetTag = assetTag,
                DeviceName = $"atomic-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
                Reason = "Create the missing asset record",
                Version = retryCurrent.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, succeeded.StatusCode);
        var created = await succeeded.Content.ReadFromJsonAsync<
            CreateAndLinkManagedAgentAssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(created.Asset.Id, created.Agent.Agent.LinkedAssetId);
        Assert.Equal(assetTag, created.Asset.AssetTag);
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.asset.created_and_linked"));
    }

    [Fact]
    public async Task AdministratorApprovedKeyRecoveryRevokesOldKeyAndIsSingleUse()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var agentClient = _factory.CreateHttpsClient();
        const string recoveryUserAgent =
            "InventoryAgent.IntegrationTests/1.0";
        agentClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            recoveryUserAgent);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Key recovery {suffix}",
            maxEnrollments: 1);
        var deviceId = $"recover-{suffix}";
        var identity = new AgentIdentityRequest
        {
            Hostname = $"recover-{suffix}.school.example",
            SerialNumber = $"RECOVER-SERIAL-{suffix}",
            BiosUuid = Guid.NewGuid().ToString(),
            Manufacturer = "Example",
            Model = "Recovery",
        };
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var enrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                deviceId,
                identity,
                oldKey),
            ApiTestClient.JsonOptions);
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrolled = await enrollmentResponse.Content.ReadFromJsonAsync<
            AgentEnrollmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);

        var current = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        var request = new CreateAgentKeyRecoveryRequest
        {
            Reason = "TPM was cleared during a motherboard repair",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20),
            RequireTpm = false,
            Version = current.Agent.Version,
        };
        using var unverifiedHardwareApproval = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery",
            new CreateAgentKeyRecoveryRequest
            {
                Reason = request.Reason,
                ExpiresAtUtc = request.ExpiresAtUtc,
                RequireTpm = true,
                Version = request.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, unverifiedHardwareApproval.StatusCode);
        Assert.Contains(
            "agent_tpm_attestation_unavailable",
            await unverifiedHardwareApproval.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var forbidden = await technician.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery",
            request,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var approval = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery",
            request,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
        Assert.Contains(
            "no-store",
            approval.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        var approved = await approval.Content.ReadFromJsonAsync<
            CreatedAgentKeyRecoveryResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(approved);
        using var forbiddenList = await technician.GetAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);
        using var activeListResponse = await administrator.GetAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery");
        activeListResponse.EnsureSuccessStatusCode();
        var activeListJson =
            await activeListResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "recoveryToken",
            activeListJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "tokenHash",
            activeListJson,
            StringComparison.OrdinalIgnoreCase);
        var activeList = JsonSerializer.Deserialize<
            PagedResponse<AgentKeyRecoveryAuthorizationResponse>>(
            activeListJson,
            ApiTestClient.JsonOptions);
        Assert.NotNull(activeList);
        var pending = Assert.Single(activeList.Items);
        Assert.Equal(approved.AuthorizationId, pending.AuthorizationId);
        Assert.Equal(
            AgentKeyRecoveryAuthorizationStatusContract.Pending,
            pending.Status);
        Assert.Equal(request.Reason, pending.Reason);
        Assert.NotEqual(Guid.Empty, pending.CreatedByUserId);
        Assert.False(string.IsNullOrWhiteSpace(
            pending.CreatedByDisplayName));

        var oldKeyCheckIn = CreateCheckInRequest(
            enrolled.AgentId,
            enrolled.DeviceId,
            1,
            CreateSnapshot(
                identity.SerialNumber!,
                Guid.Parse(identity.BiosUuid!),
                identity.Hostname),
            oldKey);
        using var oldKeyRejected = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            oldKeyCheckIn,
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldKeyRejected.StatusCode);

        using var replacementKey =
            ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var recoveredResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                approved.RecoveryToken,
                deviceId,
                identity,
                replacementKey),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, recoveredResponse.StatusCode);
        var recovered = await recoveredResponse.Content.ReadFromJsonAsync<
            AgentEnrollmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(recovered);
        Assert.Equal(enrolled.AgentId, recovered.AgentId);
        Assert.Equal(1, recovered.NextSequence);

        using var replay = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                approved.RecoveryToken,
                deviceId,
                identity,
                replacementKey),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var staleOldKey = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                1,
                CreateSnapshot(
                    identity.SerialNumber!,
                    Guid.Parse(identity.BiosUuid!),
                    identity.Hostname),
                oldKey),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, staleOldKey.StatusCode);

        using var replacementCheckIn = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                recovered.AgentId,
                recovered.DeviceId,
                recovered.NextSequence,
                CreateSnapshot(
                    identity.SerialNumber!,
                    Guid.Parse(identity.BiosUuid!),
                    identity.Hostname),
                replacementKey),
            ApiTestClient.JsonOptions);
        replacementCheckIn.EnsureSuccessStatusCode();

        var afterRecovery = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(afterRecovery);
        Assert.Equal(ManagedAgentStatusContract.Active, afterRecovery.Agent.Status);
        Assert.Equal(1, afterRecovery.Agent.KeyReplacementCount);
        Assert.NotNull(afterRecovery.Agent.KeyReplacedAtUtc);
        Assert.Equal(
            ManagedAgentMatchStateContract.Conflict,
            afterRecovery.Agent.MatchState);
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.key_recovery.approved"));
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.key_recovered"));

        await using var auditScope =
            _factory.Services.CreateAsyncScope();
        var dbContext = auditScope.ServiceProvider
            .GetRequiredService<InventoryDbContext>();
        var approvalAudit = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(
                audit =>
                    audit.TargetType == "managed_agent" &&
                    audit.TargetId == enrolled.AgentId.ToString() &&
                    audit.Action == "agent.key_recovery.approved");
        var recoveryAudit = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(
                audit =>
                    audit.TargetType == "managed_agent" &&
                    audit.TargetId == enrolled.AgentId.ToString() &&
                    audit.Action == "agent.key_recovered");
        Assert.Null(recoveryAudit.ActorUserId);
        Assert.Equal(
            "Endpoint agent",
            recoveryAudit.ActorDisplayName);
        Assert.Equal(recoveryUserAgent, recoveryAudit.Client);
        Assert.False(string.IsNullOrWhiteSpace(
            recoveryAudit.CorrelationId));
        using var recoveryDetails = JsonDocument.Parse(
            Assert.IsType<string>(recoveryAudit.DetailsJson));
        Assert.Equal(
            approvalAudit.ActorUserId,
            recoveryDetails.RootElement
                .GetProperty("ApprovedByUserId")
                .GetGuid());
        Assert.Equal(
            approvalAudit.ActorDisplayName,
            recoveryDetails.RootElement
                .GetProperty("ApprovedByDisplayName")
                .GetString());
    }

    [Fact]
    public async Task AdministratorCanCancelKeyRecoveryAndCancelledTokenIsRejected()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Cancelled key recovery {suffix}",
            maxEnrollments: 1);
        var deviceId = $"cancel-recovery-{suffix}";
        var identity = new AgentIdentityRequest
        {
            Hostname =
                $"cancel-recovery-{suffix}.school.example",
            SerialNumber = $"CANCEL-RECOVERY-{suffix}",
            BiosUuid = Guid.NewGuid().ToString(),
            Manufacturer = "Example",
            Model = "Recovery cancellation",
        };
        using var oldKey =
            ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var enrollmentResponse =
            await agentClient.PostAsJsonAsync(
                AgentProtocol.EnrollmentPath,
                CreateEnrollmentRequest(
                    profile.EnrollmentToken,
                    deviceId,
                    identity,
                    oldKey),
                ApiTestClient.JsonOptions);
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrolled = await enrollmentResponse.Content
            .ReadFromJsonAsync<AgentEnrollmentResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);

        var current = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        using var approval = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery",
            new CreateAgentKeyRecoveryRequest
            {
                Reason = "Approve replacement before cancellation",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20),
                RequireTpm = false,
                Version = current.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
        var approved = await approval.Content.ReadFromJsonAsync<
            CreatedAgentKeyRecoveryResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(approved);
        var revokeRequest = new RevokeAgentKeyRecoveryRequest
        {
            Reason = "Hardware repair was cancelled",
            Version = approved.Version,
        };
        var revokePath =
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery/" +
            $"{approved.AuthorizationId}/revoke";

        using var forbidden = await technician.PostAsJsonAsync(
            revokePath,
            revokeRequest,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var revokedResponse =
            await administrator.PostAsJsonAsync(
                revokePath,
                revokeRequest,
                ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, revokedResponse.StatusCode);
        var revoked = await revokedResponse.Content.ReadFromJsonAsync<
            AgentKeyRecoveryAuthorizationResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(revoked);
        Assert.Equal(approved.AuthorizationId, revoked.AuthorizationId);
        Assert.NotNull(revoked.RevokedAtUtc);
        Assert.Equal(revokeRequest.Reason, revoked.RevokedReason);
        Assert.Equal(
            AgentKeyRecoveryAuthorizationStatusContract.Revoked,
            revoked.Status);

        var pendingAfterRevoke = await administrator.GetFromJsonAsync<
            PagedResponse<AgentKeyRecoveryAuthorizationResponse>>(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery",
            ApiTestClient.JsonOptions);
        Assert.NotNull(pendingAfterRevoke);
        Assert.Empty(pendingAfterRevoke.Items);
        var closed = await administrator.GetFromJsonAsync<
            PagedResponse<AgentKeyRecoveryAuthorizationResponse>>(
            $"/api/v1/agents/{enrolled.AgentId}/key-recovery" +
            "?includeClosed=true",
            ApiTestClient.JsonOptions);
        Assert.NotNull(closed);
        Assert.Equal(
            AgentKeyRecoveryAuthorizationStatusContract.Revoked,
            Assert.Single(closed.Items).Status);

        using var replacementKey =
            ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cancelledTokenResponse =
            await agentClient.PostAsJsonAsync(
                AgentProtocol.EnrollmentPath,
                CreateEnrollmentRequest(
                    approved.RecoveryToken,
                    deviceId,
                    identity,
                    replacementKey),
                ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            cancelledTokenResponse.StatusCode);
        Assert.Equal(
            1,
            await CountAgentAuditAsync(
                enrolled.AgentId,
                "agent.key_recovery.revoked"));
    }

    [Fact]
    public async Task RevokingLinkedAgentAtomicallyReleasesAssetForReplacement()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var bios = Guid.NewGuid();
        var serial = $"REVOKE-SERIAL-{suffix}";
        var asset = await CreateAssetAsync(
            administrator,
            $"revoke-{suffix}",
            serial,
            bios);
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Revoke release {suffix}",
            maxEnrollments: 2);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = new AgentIdentityRequest
        {
            Hostname = $"revoke-{suffix}.school.example",
            SerialNumber = serial,
            BiosUuid = bios.ToString(),
            Manufacturer = "Example",
            Model = "Revoke",
        };
        using var enrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"revoke-device-{suffix}",
                identity,
                key),
            ApiTestClient.JsonOptions);
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrolled = await enrollmentResponse.Content.ReadFromJsonAsync<
            AgentEnrollmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);
        using var checkInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                1,
                CreateSnapshot(serial, bios, identity.Hostname),
                key),
            ApiTestClient.JsonOptions);
        checkInResponse.EnsureSuccessStatusCode();

        var current = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        Assert.Equal(asset.Id, current.Agent.LinkedAssetId);
        using var blockedRetire = await administrator.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/retire",
            new RetireAssetRequest
            {
                Reason = "A linked endpoint cannot be retired",
                Version = asset.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blockedRetire.StatusCode);
        var retireProblem =
            await blockedRetire.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "asset_has_linked_agent",
            retireProblem.GetProperty("code").GetString());

        using var blockedDispose = await administrator.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/dispose",
            new DisposeAssetRequest
            {
                Reason = "A linked endpoint cannot be disposed",
                Version = asset.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blockedDispose.StatusCode);
        var disposeProblem =
            await blockedDispose.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "asset_has_linked_agent",
            disposeProblem.GetProperty("code").GetString());

        using var revokeResponse = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/revoke",
            new RevokeManagedAgentRequest
            {
                Reason = "Replace compromised endpoint trust",
                Version = current.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        revokeResponse.EnsureSuccessStatusCode();
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<
            ManagedAgentDetailResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(revoked);
        Assert.Null(revoked.Agent.LinkedAssetId);
        Assert.Equal(ManagedAgentStatusContract.Revoked, revoked.Agent.Status);

        var replacement = await EnrollUnmatchedAgentAsync(
            agentClient,
            profile.EnrollmentToken,
            $"replacement-{suffix}",
            $"replacement-{suffix}.school.example");
        var replacementDetail = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{replacement.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(replacementDetail);
        using var linkResponse = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{replacement.AgentId}/link",
            new LinkManagedAgentRequest
            {
                AssetId = asset.Id,
                Reason = "Replacement endpoint now owns this asset",
                Version = replacementDetail.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        linkResponse.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalAssetCannotBeAutomaticallyOrManuallyLinked(
        bool disposeAfterRetirement)
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var serial = $"TERMINAL-SERIAL-{suffix}";
        var bios = Guid.NewGuid();
        var asset = await CreateAssetAsync(
            administrator,
            $"terminal-{suffix}",
            serial,
            bios);
        using var retireResponse = await administrator.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/retire",
            new RetireAssetRequest
            {
                Version = asset.Version,
                Reason = "Endpoint is no longer in operational service",
            },
            ApiTestClient.JsonOptions);
        retireResponse.EnsureSuccessStatusCode();
        var terminal = await retireResponse.Content.ReadFromJsonAsync<
            AssetResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(terminal);
        if (disposeAfterRetirement)
        {
            using var disposeResponse = await administrator.PostAsJsonAsync(
                $"/api/v1/assets/{asset.Id}/dispose",
                new DisposeAssetRequest
                {
                    Version = terminal.Version,
                    Reason = "Certified disposal after retirement",
                },
                ApiTestClient.JsonOptions);
            disposeResponse.EnsureSuccessStatusCode();
            terminal = await disposeResponse.Content.ReadFromJsonAsync<
                AssetResponse>(ApiTestClient.JsonOptions);
            Assert.NotNull(terminal);
        }

        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Terminal matching {suffix}",
            maxEnrollments: 1);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = new AgentIdentityRequest
        {
            Hostname = $"terminal-{suffix}.school.example",
            SerialNumber = serial,
            BiosUuid = bios.ToString(),
            Manufacturer = "Example",
            Model = "Terminal",
        };
        using var enrollResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"terminal-device-{suffix}",
                identity,
                key),
            ApiTestClient.JsonOptions);
        enrollResponse.EnsureSuccessStatusCode();
        var enrolled = await enrollResponse.Content.ReadFromJsonAsync<
            AgentEnrollmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);
        using var checkInResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                1,
                CreateSnapshot(serial, bios, identity.Hostname),
                key),
            ApiTestClient.JsonOptions);
        checkInResponse.EnsureSuccessStatusCode();
        var checkedIn = await checkInResponse.Content.ReadFromJsonAsync<
            AgentCheckInResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(checkedIn);
        Assert.Null(checkedIn.LinkedAssetId);

        var current = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        Assert.Equal(
            ManagedAgentMatchStateContract.Conflict,
            current.Agent.MatchState);
        Assert.Contains(
            "retired or disposed",
            current.Agent.MatchReason,
            StringComparison.OrdinalIgnoreCase);
        using var manualLink = await administrator.PostAsJsonAsync(
            $"/api/v1/agents/{enrolled.AgentId}/link",
            new LinkManagedAgentRequest
            {
                AssetId = terminal.Id,
                Reason = "This link must be rejected",
                Version = current.Agent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, manualLink.StatusCode);
    }

    [Fact]
    public async Task OrderedOfflineBacklogIsAcceptedThenPerAgentBurstIsLimited()
    {
        using var administrator =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var agentClient = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var profile = await CreateEnrollmentProfileAsync(
            administrator,
            $"Backlog {suffix}",
            maxEnrollments: 1);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bios = Guid.NewGuid();
        var serial = $"BACKLOG-SERIAL-{suffix}";
        var identity = new AgentIdentityRequest
        {
            Hostname = $"backlog-{suffix}.school.example",
            SerialNumber = serial,
            BiosUuid = bios.ToString(),
            Manufacturer = "Example",
            Model = "Backlog",
        };
        using var enrollmentResponse = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            CreateEnrollmentRequest(
                profile.EnrollmentToken,
                $"backlog-device-{suffix}",
                identity,
                key),
            ApiTestClient.JsonOptions);
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrolled = await enrollmentResponse.Content.ReadFromJsonAsync<
            AgentEnrollmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(enrolled);

        Guid latestSnapshotId = Guid.Empty;
        for (var sequence = 1; sequence <= 144; sequence++)
        {
            var snapshot = CreateSnapshot(serial, bios, identity.Hostname);
            latestSnapshotId = snapshot.SnapshotId;
            using var accepted = await agentClient.PostAsJsonAsync(
                AgentProtocol.CheckInPath,
                CreateCheckInRequest(
                    enrolled.AgentId,
                    enrolled.DeviceId,
                    sequence,
                    snapshot,
                    key),
                ApiTestClient.JsonOptions);
            accepted.EnsureSuccessStatusCode();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var retained = await dbContext.ManagedAgentSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.AgentId == enrolled.AgentId)
                .ToArrayAsync();
            Assert.InRange(retained.Length, 1, 65);
            Assert.Single(
                retained,
                snapshot => snapshot.PayloadJson is not null);
            var agent = await dbContext.ManagedAgents
                .AsNoTracking()
                .SingleAsync(item => item.Id == enrolled.AgentId);
            Assert.Equal(144, agent.CheckInBurstCount);
        }

        var detail = await administrator.GetFromJsonAsync<
            ManagedAgentDetailResponse>(
            $"/api/v1/agents/{enrolled.AgentId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(latestSnapshotId, detail.LatestSnapshot?.SnapshotId);
        Assert.Equal(144, detail.Agent.LastSequence);

        using var limited = await agentClient.PostAsJsonAsync(
            AgentProtocol.CheckInPath,
            CreateCheckInRequest(
                enrolled.AgentId,
                enrolled.DeviceId,
                145,
                CreateSnapshot(serial, bios, identity.Hostname),
                key),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        string suffix,
        string serialNumber,
        Guid biosUuid)
    {
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"AGT-{suffix}",
                Name = "Managed endpoint",
            },
            ApiTestClient.JsonOptions);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content
            .ReadFromJsonAsync<CategoryResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(category);

        var assetResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"AGENT-{suffix}",
                DeviceName = $"pc-{suffix}",
                CategoryId = category.Id,
                SerialNumber = serialNumber,
                BiosGuid = biosUuid,
            },
            ApiTestClient.JsonOptions);
        assetResponse.EnsureSuccessStatusCode();
        return await assetResponse.Content.ReadFromJsonAsync<AssetResponse>(
                ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The integration asset was not returned.");
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string code,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = code,
                Name = name,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CategoryResponse>(
                ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The integration category was not returned.");
    }

    private static async Task<AgentEnrollmentResponse>
        EnrollUnmatchedAgentAsync(
            HttpClient agentClient,
            string enrollmentToken,
            string deviceId,
            string hostname)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = CreateEnrollmentRequest(
            enrollmentToken,
            deviceId,
            new AgentIdentityRequest
            {
                Hostname = hostname,
                SerialNumber = $"SERIAL-{deviceId}",
                BiosUuid = Guid.NewGuid().ToString(),
                Manufacturer = "Example manufacturer",
                Model = "Example model",
            },
            key);
        using var response = await agentClient.PostAsJsonAsync(
            AgentProtocol.EnrollmentPath,
            request,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<
                AgentEnrollmentResponse>(ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The enrolled endpoint agent was not returned.");
    }

    private static async Task<ManagedAgentDetailResponse>
        PostAgentActionAsync<TRequest>(
            HttpClient client,
            Guid agentId,
            string action,
            TRequest request)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{agentId}/{action}",
            request,
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<
                ManagedAgentDetailResponse>(ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                $"The {action} response was not returned.");
    }

    private async Task<int> CountAgentAuditAsync(
        Guid agentId,
        string action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs
            .AsNoTracking()
            .CountAsync(
                audit =>
                    audit.TargetType == "managed_agent" &&
                    audit.TargetId == agentId.ToString() &&
                    audit.Action == action);
    }

    private static async Task<CreatedAgentEnrollmentProfileResponse>
        CreateEnrollmentProfileAsync(
            HttpClient client,
            string name,
            int maxEnrollments)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/agents/enrollment-profiles",
            new CreateAgentEnrollmentProfileRequest
            {
                Name = name,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                MaxEnrollments = maxEnrollments,
                RequireTpm = false,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        return await response.Content
                .ReadFromJsonAsync<CreatedAgentEnrollmentProfileResponse>(
                    ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The enrollment profile was not returned.");
    }

    private static AgentEnrollmentRequest CreateEnrollmentRequest(
        string token,
        string deviceId,
        AgentIdentityRequest identity,
        ECDsa key)
    {
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var unsigned = new AgentEnrollmentRequest
        {
            EnrollmentToken = token,
            TimestampEpochSeconds =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32)),
            DeviceId = deviceId,
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem(),
            SignatureAlgorithm = AgentProtocol.SignatureAlgorithm,
            KeyProtection = AgentKeyProtectionContract.Tpm,
            KeyThumbprintSha256 = Convert.ToHexString(
                    SHA256.HashData(publicKey))
                .ToLowerInvariant(),
            Identity = identity,
            Signature = string.Empty,
        };
        return new AgentEnrollmentRequest
        {
            EnrollmentToken = unsigned.EnrollmentToken,
            TimestampEpochSeconds = unsigned.TimestampEpochSeconds,
            Nonce = unsigned.Nonce,
            DeviceId = unsigned.DeviceId,
            PublicKeyPem = unsigned.PublicKeyPem,
            SignatureAlgorithm = unsigned.SignatureAlgorithm,
            KeyProtection = unsigned.KeyProtection,
            KeyThumbprintSha256 = unsigned.KeyThumbprintSha256,
            Identity = unsigned.Identity,
            Signature = Sign(
                key,
                AgentProtocol.BuildEnrollmentCanonical(unsigned)),
        };
    }

    private static AgentCheckInRequest CreateCheckInRequest(
        Guid agentId,
        string deviceId,
        long sequence,
        AgentDeviceSnapshotRequest snapshot,
        ECDsa key)
    {
        var unsigned = new AgentCheckInRequest
        {
            AgentId = agentId,
            DeviceId = deviceId,
            Sequence = sequence,
            TimestampEpochSeconds =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32)),
            BodySha256 = AgentProtocol.HashSnapshot(snapshot),
            Signature = string.Empty,
            Snapshot = snapshot,
        };
        return CopyWithSignature(
            unsigned,
            Sign(key, AgentProtocol.BuildCheckInCanonical(unsigned)));
    }

    private static AgentCheckInRequest CopyWithSignature(
        AgentCheckInRequest request,
        string signature) =>
        new()
        {
            AgentId = request.AgentId,
            DeviceId = request.DeviceId,
            Sequence = request.Sequence,
            TimestampEpochSeconds = request.TimestampEpochSeconds,
            Nonce = request.Nonce,
            BodySha256 = request.BodySha256,
            Signature = signature,
            Snapshot = request.Snapshot,
        };

    private static AgentDeviceSnapshotRequest CreateSnapshot(
        string serialNumber,
        Guid biosUuid,
        string hostname) =>
        new()
        {
            SnapshotId = Guid.NewGuid(),
            CollectedAtUtc = DateTimeOffset.UtcNow,
            AgentVersion = "1.0.0",
            Hostname = hostname,
            Domain = "school.example",
            Hardware = new AgentHardwareSnapshotRequest
            {
                Manufacturer = "Dell",
                Model = "Latitude",
                SerialNumber = serialNumber,
                BiosUuid = biosUuid.ToString(),
                Processor = "Example CPU",
                ProcessorCores = 8,
                LogicalProcessors = 16,
                MemoryBytes = 16L * 1024 * 1024 * 1024,
            },
            OperatingSystem = new AgentOperatingSystemSnapshotRequest
            {
                Name = "Windows 11 Enterprise",
                Version = "10.0",
                Build = "26100",
                Architecture = "64-bit",
            },
            Security = new AgentSecuritySnapshotRequest
            {
                SecureBootEnabled = true,
                TpmPresent = true,
                TpmEnabled = true,
                TpmVersion = "2.0",
                SystemDriveProtected = true,
                AntivirusProducts = "Microsoft Defender Antivirus",
            },
            NetworkAdapters =
            [
                new AgentNetworkAdapterSnapshotRequest
                {
                    Name = "Ethernet",
                    Type = "Ethernet",
                    MacAddress = "74:86:E2:3A:00:96",
                    Ipv4Addresses = ["192.0.2.10"],
                },
            ],
            StorageVolumes =
            [
                new AgentStorageVolumeSnapshotRequest
                {
                    Name = "C:\\",
                    Label = "Windows",
                    FileSystem = "NTFS",
                    TotalBytes = 512L * 1024 * 1024 * 1024,
                    FreeBytes = 256L * 1024 * 1024 * 1024,
                },
            ],
            InstalledSoftware =
            [
                new AgentInstalledSoftwareSnapshotRequest
                {
                    Name = "Microsoft 365 Apps",
                    Version = "16.0",
                    Publisher = "Microsoft Corporation",
                    Architecture = "x64",
                },
            ],
        };

    private static string Sign(ECDsa key, string canonical) =>
        Convert.ToBase64String(
            key.SignData(
                Encoding.UTF8.GetBytes(canonical),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private static AgentIdentityRequest WithHostname(
        AgentIdentityRequest identity,
        string hostname) =>
        new()
        {
            Hostname = hostname,
            SerialNumber = identity.SerialNumber,
            BiosUuid = identity.BiosUuid,
            Manufacturer = identity.Manufacturer,
            Model = identity.Model,
        };
}
