using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.DataExchange;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class ImportExportApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ImportExportApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidateCommitExportAndRoundTripPreserveAssetData()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var location = await CreateLocationAsync(client, suffix);
        var person = await CreatePersonAsync(client, suffix);
        var assetTag = $"IMP-{suffix}";
        var notes = "=HYPERLINK(\"https://example.invalid\",\"Inventory\")";
        var csv = BuildCsv(
            [
                Row(
                    assetTag,
                    category.Code,
                    deviceName: "STR-ICT-TEST-01",
                    description: "ICT support test laptop",
                    serialNumber: $"SER-{suffix}",
                    lanMacAddress: "74:86:e2:3a:00:96",
                    biosGuid: "4C4C4544-0030-3310-8043-C3C04F475233",
                    ownership: "Leased",
                    status: "Assigned",
                    locationCode: location.Code,
                    assignedPersonExternalId: person.ExternalId,
                    purchaseDate: "2026-01-15",
                    purchaseCost: "1250.50",
                    currency: "GBP",
                    warrantyStartDate: "2026-01-15",
                    warrantyEndDate: "2029-01-14",
                    warrantyProvider: "School Supplier",
                    warrantyReference: $"WAR-{suffix}",
                    operatingSystem: "Windows",
                    operatingSystemVersion: "11",
                    windows11Compliant: "true",
                    legacyId: $"LEG-{suffix}",
                    notes: notes),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Created, validateResponse.StatusCode);
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Equal(1, job.ValidRows);
        Assert.Equal(1, job.CreateRows);
        Assert.Empty(job.Errors);
        Assert.Equal(AssetImportActionContract.Create, Assert.Single(job.Preview).Action);

        using var beforeCommit = await client.GetAsync(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(assetTag)}");
        Assert.Equal(HttpStatusCode.NotFound, beforeCommit.StatusCode);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();
        var commit = await commitResponse.Content
            .ReadFromJsonAsync<AssetImportCommitResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(commit);
        Assert.Equal(1, commit.CreatedRows);
        Assert.Equal(0, commit.UpdatedRows);

        var asset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(assetTag)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(asset);
        Assert.Equal(AssetStatusContract.Assigned, asset.Status);
        Assert.Equal(person.Id, asset.CurrentAssignment?.Target.Id);
        Assert.Equal("School Supplier", asset.WarrantySummary.Provider);
        Assert.Equal(1250.50m, asset.PurchaseCost);
        Assert.Equal("STR-ICT-TEST-01", asset.DeviceName);
        Assert.Equal("ICT support test laptop", asset.Description);
        Assert.Equal("74:86:E2:3A:00:96", asset.LanMacAddress);
        Assert.Equal(
            Guid.Parse("4C4C4544-0030-3310-8043-C3C04F475233"),
            asset.BiosGuid);
        Assert.Equal("Leased", asset.Ownership);
        Assert.Equal("Windows", asset.OperatingSystem);
        Assert.Equal("11", asset.OperatingSystemVersion);
        Assert.True(asset.Windows11Compliant);
        Assert.Equal(notes, asset.Notes);

        var history = await client.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        var importEvent = Assert.Single(
            history?.Items.Where(item => item.Type == "ImportCreated") ?? []);
        Assert.Equal("Import", importEvent.Source);

        using var exportResponse = await client.GetAsync(
            $"/api/v1/exports/assets.csv?assetTag={Uri.EscapeDataString(assetTag)}");
        exportResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            "no-store",
            exportResponse.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal("1", Assert.Single(exportResponse.Headers.GetValues(
            "X-Inventory-Row-Count")));
        var exported = await exportResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], exported[..3]);
        var exportedDocument = AssetCsvCodec.Parse(
            AssetCsvCodec.DecodeStrictUtf8(exported));
        Assert.Equal(AssetCsvSchema.Headers, exportedDocument.Records[0]);
        var notesIndex = AssetCsvSchema.Headers
            .Select((header, index) => new { header, index })
            .Single(item => item.header == "notes")
            .index;
        Assert.Equal(
            notes,
            AssetCsvCodec.RemoveFormulaProtection(
                exportedDocument.Records[1][notesIndex]));

        using var roundTripValidationResponse = await UploadAsync(
            client,
            exported,
            AssetImportModeContract.UpsertByAssetTag);
        roundTripValidationResponse.EnsureSuccessStatusCode();
        var roundTripJob = await roundTripValidationResponse.Content
            .ReadFromJsonAsync<AssetImportJobResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(roundTripJob);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, roundTripJob.Status);
        Assert.Equal(1, roundTripJob.UpdateRows);

        using var roundTripCommitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{roundTripJob.Id}/commit",
            new CommitAssetImportRequest { Version = roundTripJob.Version },
            ApiTestClient.JsonOptions);
        roundTripCommitResponse.EnsureSuccessStatusCode();
        var roundTripCommit = await roundTripCommitResponse.Content
            .ReadFromJsonAsync<AssetImportCommitResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(roundTripCommit);
        Assert.Equal(1, roundTripCommit.UnchangedRows);

        var auditActions = await GetAuditActionsAsync();
        Assert.True(auditActions.Count(action => action == "assets.import.validated") >= 2);
        Assert.True(auditActions.Count(action => action == "assets.import.committed") >= 2);
        Assert.Contains("assets.exported", auditActions);
    }

    [Fact]
    public async Task StrLegacyRegisterCanValidateCommitAndPreserveItsAssetFields()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var location = await CreateLocationAsync(client, suffix);
        var person = await CreatePersonAsync(client, suffix);
        var assetTag = $"STR-{suffix}";
        var biosGuid = Guid.Parse("8A14AA59-A79F-438F-9B7B-DE37C1E0F2D9");
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.StrLegacyHeaders,
            [
                LegacyRow(
                    deviceName: assetTag,
                    assetType: category.Code,
                    description: "Finance office desktop",
                    serialNumber: $"LEG-SER-{suffix}",
                    lanMacAddress: "B0:22:7A:24:77:F2",
                    biosGuid: biosGuid.ToString("D"),
                    usedBy: person.ExternalId,
                    location: location.Name,
                    product: "HP ProDesk 400 G7 SFF",
                    vendor: "HP",
                    ownership: "Leased",
                    cost: "362.63",
                    warranty: "Yes",
                    acquisitionDate: "30/07/2021",
                    warrantyExpiryDate: "30/07/2024",
                    assetState: "In Use",
                    operatingSystem: "Windows",
                    operatingSystemVersion: "11",
                    windows11Compliant: "Yes"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(2, job.SchemaVersion);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Empty(job.Errors);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();

        var asset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(assetTag)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(asset);
        Assert.Equal(assetTag, asset.DeviceName);
        Assert.Equal("Finance office desktop", asset.Description);
        Assert.Equal("HP", asset.Manufacturer);
        Assert.Equal("HP ProDesk 400 G7 SFF", asset.Model);
        Assert.Equal("B0:22:7A:24:77:F2", asset.LanMacAddress);
        Assert.Equal(biosGuid, asset.BiosGuid);
        Assert.Equal("Leased", asset.Ownership);
        Assert.Equal("Windows", asset.OperatingSystem);
        Assert.Equal("11", asset.OperatingSystemVersion);
        Assert.True(asset.Windows11Compliant);
        Assert.Equal(362.63m, asset.PurchaseCost);
        Assert.Equal("GBP", asset.Currency);
        Assert.Equal(AssetStatusContract.Assigned, asset.Status);
        Assert.Equal(person.Id, asset.CurrentAssignment?.Target.Id);
        Assert.Equal("Legacy asset register", asset.WarrantySummary.Provider);
        Assert.Equal(new DateOnly(2024, 7, 30), asset.WarrantySummary.EndsOn);
    }

    [Fact]
    public async Task StrLegacyPeopleUseSafeFuzzyMatchingAndFormerHoldersRemainUnassigned()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var fuzzyPerson = await CreatePersonAsync(
            client,
            suffix,
            externalId: $"schappell{suffix}",
            displayName: "Sarah Chappell");
        _ = await CreatePersonAsync(
            client,
            suffix,
            externalId: $"jgreen{suffix}",
            displayName: "John Green");
        var fuzzyTag = $"FUZZ-{suffix}";
        var differentInitialTag = $"INITIAL-{suffix}";
        var formerHolderTag = $"FORMER-{suffix}";
        var fuzzySource = $"schappel{suffix}";
        var differentInitialSource = $"tgreen{suffix}";
        var formerHolderSource = $"leftstaff{suffix}";
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.StrLegacyHeaders,
            [
                LegacyRow(
                    deviceName: fuzzyTag,
                    assetType: category.Code,
                    usedBy: fuzzySource),
                LegacyRow(
                    deviceName: differentInitialTag,
                    assetType: category.Code,
                    usedBy: differentInitialSource),
                LegacyRow(
                    deviceName: formerHolderTag,
                    assetType: category.Code,
                    usedBy: formerHolderSource),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Empty(job.Errors);
        Assert.Equal(1, job.FuzzyPersonMatchRows);
        Assert.Equal(2, job.UnassignedImportRows);

        var fuzzyPreview = Assert.Single(
            job.Preview,
            row => row.AssetTag == fuzzyTag);
        Assert.Equal(fuzzySource, fuzzyPreview.AssignmentSource);
        Assert.Equal(
            AssetImportAssignmentResolutionContract.FuzzyPerson,
            fuzzyPreview.AssignmentResolution);
        Assert.Equal("Sarah Chappell", fuzzyPreview.AssignedTo);
        Assert.Equal(AssetStatusContract.Assigned, fuzzyPreview.Status);

        foreach (var tag in new[] { differentInitialTag, formerHolderTag })
        {
            var preview = Assert.Single(job.Preview, row => row.AssetTag == tag);
            Assert.Equal(
                AssetImportAssignmentResolutionContract.Unassigned,
                preview.AssignmentResolution);
            Assert.Equal(AssetStatusContract.InStock, preview.Status);
            Assert.Null(preview.AssignedTo);
        }

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();

        var fuzzyAsset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(fuzzyTag)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(fuzzyAsset);
        Assert.Equal(AssetStatusContract.Assigned, fuzzyAsset.Status);
        Assert.Equal(fuzzyPerson.Id, fuzzyAsset.CurrentAssignment?.Target.Id);

        foreach (var tag in new[] { differentInitialTag, formerHolderTag })
        {
            var asset = await client.GetFromJsonAsync<AssetResponse>(
                $"/api/v1/assets/by-tag/{Uri.EscapeDataString(tag)}",
                ApiTestClient.JsonOptions);
            Assert.NotNull(asset);
            Assert.Equal(AssetStatusContract.InStock, asset.Status);
            Assert.Null(asset.CurrentAssignment);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var commitAudit = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(
                audit =>
                    audit.TargetType == "asset_import_job" &&
                    audit.TargetId == job.Id.ToString() &&
                    audit.Action == "assets.import.committed");
        Assert.Contains(
            "\"fuzzyPersonMatchRows\":1",
            commitAudit.DetailsJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"unassignedImportRows\":2",
            commitAudit.DetailsJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCanLeaveAssignedRowsUnassignedButManualCreationRemainsStrict()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var missingDepartmentTag = $"NO-DEPT-{suffix}";
        var missingTargetTag = $"NO-TARGET-{suffix}";
        var csv = BuildCsv(
            [
                Row(
                    missingDepartmentTag,
                    category.Code,
                    status: "Assigned",
                    departmentCode: $"FORMER-DEPT-{suffix}"),
                Row(
                    missingTargetTag,
                    category.Code,
                    status: "Assigned"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Empty(job.Errors);
        Assert.Equal(2, job.UnassignedImportRows);
        Assert.All(
            job.Preview,
            row =>
            {
                Assert.Equal(
                    AssetImportAssignmentResolutionContract.Unassigned,
                    row.AssignmentResolution);
                Assert.Equal(AssetStatusContract.InStock, row.Status);
                Assert.Null(row.AssignedTo);
            });

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();

        foreach (var tag in new[] { missingDepartmentTag, missingTargetTag })
        {
            var asset = await client.GetFromJsonAsync<AssetResponse>(
                $"/api/v1/assets/by-tag/{Uri.EscapeDataString(tag)}",
                ApiTestClient.JsonOptions);
            Assert.NotNull(asset);
            Assert.Equal(AssetStatusContract.InStock, asset.Status);
            Assert.Null(asset.CurrentAssignment);
        }

        using var manualResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"MANUAL-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.Assigned,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, manualResponse.StatusCode);
    }

    [Fact]
    public async Task CanonicalVersion1RemainsAcceptedAfterVersion2IsIntroduced()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var assetTag = $"V1-{suffix}";
        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = assetTag,
                DeviceName = $"STR-V1-{suffix}",
                CategoryId = category.Id,
                Description = "Must survive a version-1 upsert",
                Manufacturer = "Dell",
                Model = "Latitude",
                SerialNumber = $"V1-SER-{suffix}",
                LanMacAddress = "00:11:22:33:44:55",
                BiosGuid = Guid.Parse("4C4C4544-0030-3310-8043-C3C04F475233"),
                Ownership = "Leased",
                OperatingSystem = "Windows",
                OperatingSystemVersion = "11",
                Windows11Compliant = true,
            },
            ApiTestClient.JsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.Version1Headers,
            [
                Version1Row(
                    assetTag,
                    category.Code,
                    serialNumber: created.SerialNumber),
            ]);

        using var response = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.UpsertByAssetTag);
        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(1, job.SchemaVersion);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Empty(job.Errors);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();
        var updated = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{created.Id}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(created.DeviceName, updated.DeviceName);
        Assert.Equal(created.Description, updated.Description);
        Assert.Equal(created.LanMacAddress, updated.LanMacAddress);
        Assert.Equal(created.BiosGuid, updated.BiosGuid);
        Assert.Equal(created.Ownership, updated.Ownership);
        Assert.Equal(created.OperatingSystem, updated.OperatingSystem);
        Assert.Equal(created.OperatingSystemVersion, updated.OperatingSystemVersion);
        Assert.Equal(created.Windows11Compliant, updated.Windows11Compliant);
    }

    [Fact]
    public async Task StrLegacyRegisterGeneratesStableTagsForBlankDeviceNames()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.StrLegacyHeaders,
            [
                LegacyRow(
                    deviceName: string.Empty,
                    assetType: category.Code,
                    description: "Monitor without a source asset tag",
                    serialNumber: $"AUTO-SER-{suffix}"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Equal(1, job.GeneratedAssetTagRows);
        var preview = Assert.Single(job.Preview);
        Assert.True(preview.AssetTagGenerated);
        Assert.Equal($"STR-AUTO-{job.FileHash[..16]}-0002", preview.AssetTag);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();

        var asset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(preview.AssetTag)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(asset);
        Assert.Null(asset.DeviceName);
        Assert.Equal("Monitor without a source asset tag", asset.Description);

        using var retryResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.UpsertByAssetTag);
        retryResponse.EnsureSuccessStatusCode();
        var retryJob = await retryResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(retryJob);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, retryJob.Status);
        Assert.Equal(1, retryJob.UpdateRows);
        Assert.Equal(preview.AssetTag, Assert.Single(retryJob.Preview).AssetTag);
    }

    [Fact]
    public async Task StrLegacyRegisterAcceptsMonthOnlyDatesButStillRejectsMultipleSchools()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.StrLegacyHeaders,
            [
                LegacyRow(
                    deviceName: string.Empty,
                    assetType: category.Code,
                    description: "Monitor linked to a desktop",
                    acquisitionDate: "11.2025",
                    warrantyExpiryDate: "11.2026"),
                LegacyRow(
                    deviceName: $"OTHER-{suffix}",
                    assetType: category.Code,
                    school: "OTHER"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ValidationFailed, job.Status);
        Assert.DoesNotContain(
            job.Errors,
            error => error.Column == "assetTag" && error.Code == "required");
        Assert.DoesNotContain(
            job.Errors,
            error => error.Column is "purchaseDate" or "warrantyEndDate");
        Assert.Contains(
            job.Errors,
            error => error.Column == "School" &&
                     error.Code == "multiple_schools_not_supported");
    }

    [Fact]
    public async Task MissingReferencesAndPartialDatesArePlannedThenCommittedAtomically()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var categoryName = $"Monitor {suffix}";
        var categoryCode = $"MONITOR-{suffix.ToUpperInvariant()}";
        var locationName = $"Data & Finance {suffix}";
        var locationCode = $"DATA-FINANCE-{suffix.ToUpperInvariant()}";
        var firstTag = $"AUTO-REF-A-{suffix}";
        var secondTag = $"AUTO-REF-B-{suffix}";
        var csv = AssetCsvCodec.Write(
            AssetCsvSchema.StrLegacyHeaders,
            [
                LegacyRow(
                    deviceName: firstTag,
                    assetType: categoryName,
                    location: locationName,
                    acquisitionDate: "2021",
                    warrantyExpiryDate: "2022"),
                LegacyRow(
                    deviceName: secondTag,
                    assetType: categoryName,
                    location: locationName,
                    acquisitionDate: "44407",
                    warrantyExpiryDate: "45502"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);
        Assert.Empty(job.Errors);
        Assert.Equal(1, job.PlannedCategoryCreateCount);
        Assert.Equal(1, job.PlannedLocationCreateCount);
        Assert.Equal(2, job.InferredDateRows);
        var inferredPreview = Assert.Single(
            job.Preview,
            preview => preview.AssetTag == firstTag);
        Assert.True(inferredPreview.CategoryWillBeCreated);
        Assert.Equal(categoryCode, inferredPreview.ResolvedCategoryCode);
        Assert.True(inferredPreview.LocationWillBeCreated);
        Assert.Equal(locationCode, inferredPreview.ResolvedLocationCode);
        Assert.Equal(new DateOnly(2021, 1, 1), inferredPreview.PurchaseDate);
        Assert.Equal(new DateOnly(2021, 1, 1), inferredPreview.WarrantyStartDate);
        Assert.Equal(new DateOnly(2022, 12, 31), inferredPreview.WarrantyEndDate);
        Assert.Equal(
            ["purchaseDate", "warrantyStartDate", "warrantyEndDate"],
            inferredPreview.InferredDateFields);
        var excelSerialPreview = Assert.Single(
            job.Preview,
            preview => preview.AssetTag == secondTag);
        Assert.Equal(new DateOnly(2021, 7, 30), excelSerialPreview.PurchaseDate);
        Assert.Equal(new DateOnly(2021, 7, 30), excelSerialPreview.WarrantyStartDate);
        Assert.Equal(new DateOnly(2024, 7, 29), excelSerialPreview.WarrantyEndDate);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.False(
                await dbContext.AssetCategories.AnyAsync(
                    category => category.CodeNormalized == categoryCode));
            Assert.False(
                await dbContext.Locations.AnyAsync(
                    location => location.CodeNormalized == locationCode));
        }

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();
        var commit = await commitResponse.Content
            .ReadFromJsonAsync<AssetImportCommitResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(commit);
        Assert.Equal(2, commit.CreatedRows);
        Assert.Equal(1, commit.CreatedCategoryCount);
        Assert.Equal(1, commit.CreatedLocationCount);

        var asset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(firstTag)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(asset);
        Assert.Equal(categoryCode, asset.Category.Code);
        Assert.Equal(categoryName, asset.Category.Name);
        Assert.Equal(locationCode, asset.Location?.Code);
        Assert.Equal(locationName, asset.Location?.Name);
        Assert.Equal(new DateOnly(2021, 1, 1), asset.PurchaseDate);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            Assert.Equal(
                1,
                await dbContext.AssetCategories.CountAsync(
                    category => category.CodeNormalized == categoryCode));
            Assert.Equal(
                1,
                await dbContext.Locations.CountAsync(
                    location => location.CodeNormalized == locationCode));
            var warranty = await dbContext.WarrantyCoverages
                .AsNoTracking()
                .SingleAsync(item => item.AssetId == asset.Id && item.IsPrimary);
            Assert.Equal(new DateOnly(2021, 1, 1), warranty.StartDate);
            Assert.Equal(new DateOnly(2022, 12, 31), warranty.EndDate);
            var commitAudit = await dbContext.AuditLogs
                .AsNoTracking()
                .SingleAsync(
                    audit =>
                        audit.TargetType == "asset_import_job" &&
                        audit.TargetId == job.Id.ToString() &&
                        audit.Action == "assets.import.committed");
            Assert.Contains(categoryCode, commitAudit.DetailsJson, StringComparison.Ordinal);
            Assert.Contains(locationCode, commitAudit.DetailsJson, StringComparison.Ordinal);
            Assert.Contains("\"inferredDateRows\":2", commitAudit.DetailsJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ValidationPersistsRowErrorsAndRejectsCommitWithoutChangingAssets()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var duplicateTag = $"BAD-{suffix}";
        var csv = BuildCsv(
            [
                Row(
                    duplicateTag,
                    category.Code,
                    status: "Assigned",
                    purchaseDate: "25/07/2026",
                    purchaseCost: "10.999",
                    currency: "ZZZ"),
                Row(
                    duplicateTag.ToLowerInvariant(),
                    "MISSING-CATEGORY",
                    serialNumber: $"SER-{suffix}"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Created, validateResponse.StatusCode);
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ValidationFailed, job.Status);
        Assert.Equal(2, job.TotalRows);
        Assert.Equal(0, job.ValidRows);
        Assert.Equal(2, job.ErrorRows);
        Assert.Contains(job.Errors, error => error.Code == "duplicate_asset_tag_in_file");
        Assert.Contains(job.Errors, error => error.Code == "invalid_purchaseDate");
        Assert.Contains(job.Errors, error => error.Code == "invalid_currency");
        Assert.DoesNotContain(job.Errors, error => error.Code == "assignment_target_required");
        Assert.DoesNotContain(job.Errors, error => error.Code == "unknown_category");

        using var errorResponse = await client.GetAsync(
            $"/api/v1/imports/{job.Id}/errors.csv");
        errorResponse.EnsureSuccessStatusCode();
        var errorText = await errorResponse.Content.ReadAsStringAsync();
        Assert.Contains("duplicate_asset_tag_in_file", errorText);
        Assert.DoesNotContain("unknown_category", errorText);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);

        using var assetResponse = await client.GetAsync(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(duplicateTag)}");
        Assert.Equal(HttpStatusCode.NotFound, assetResponse.StatusCode);
        Assert.Contains(
            "assets.import.commit_rejected",
            await GetAuditActionsAsync());
    }

    [Fact]
    public async Task ConflictAfterValidationRejectsTheWholeImportBeforeAnyRowIsWritten()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var firstTag = $"ATOMIC-A-{suffix}";
        var secondTag = $"ATOMIC-B-{suffix}";
        var csv = BuildCsv(
            [
                Row(firstTag, category.Code, serialNumber: $"SER-A-{suffix}"),
                Row(secondTag, category.Code, serialNumber: $"SER-B-{suffix}"),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);

        using var conflictingAssetResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = secondTag,
                CategoryId = category.Id,
                SerialNumber = $"OTHER-{suffix}",
            },
            ApiTestClient.JsonOptions);
        conflictingAssetResponse.EnsureSuccessStatusCode();

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);

        using var firstAssetResponse = await client.GetAsync(
            $"/api/v1/assets/by-tag/{Uri.EscapeDataString(firstTag)}");
        Assert.Equal(HttpStatusCode.NotFound, firstAssetResponse.StatusCode);
        var unchangedJob = await client.GetFromJsonAsync<AssetImportJobResponse>(
            $"/api/v1/imports/{job.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, unchangedJob?.Status);
    }

    [Fact]
    public async Task LateDatabaseFailureRollsBackEveryRowAndPersistsOnlyRejectionAudit()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var categoryName = $"Rollback category {suffix}";
        var categoryCode = $"ROLLBACK-CATEGORY-{suffix.ToUpperInvariant()}";
        var locationName = $"Rollback location {suffix}";
        var locationCode = $"ROLLBACK-LOCATION-{suffix.ToUpperInvariant()}";
        var firstTag = $"LATE-A-{suffix}";
        var secondTag = $"LATE-B-{suffix}";
        var triggerName = $"TR_ImportLateFailure_{suffix}";
        var csv = BuildCsv(
            [
                Row(
                    firstTag,
                    categoryName,
                    serialNumber: $"LATE-SER-A-{suffix}",
                    locationCode: locationName),
                Row(
                    secondTag,
                    categoryName,
                    serialNumber: $"LATE-SER-B-{suffix}",
                    locationCode: locationName),
            ]);

        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.CreateOnly);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(AssetImportStatusContract.ReadyToCommit, job.Status);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var createTriggerSql =
                $"""
                CREATE TRIGGER "{triggerName}"
                BEFORE INSERT ON "Assets"
                WHEN NEW."AssetTagNormalized" = '{secondTag.ToUpperInvariant()}'
                BEGIN
                    SELECT RAISE(ABORT, 'forced_late_import_failure');
                END;
                """;
            await dbContext.Database.ExecuteSqlRawAsync(createTriggerSql);
        }

        try
        {
            using var commitResponse = await client.PostAsJsonAsync(
                $"/api/v1/imports/{job.Id}/commit",
                new CommitAssetImportRequest { Version = job.Version },
                ApiTestClient.JsonOptions);
            Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);

            using var firstAsset = await client.GetAsync(
                $"/api/v1/assets/by-tag/{Uri.EscapeDataString(firstTag)}");
            using var secondAsset = await client.GetAsync(
                $"/api/v1/assets/by-tag/{Uri.EscapeDataString(secondTag)}");
            Assert.Equal(HttpStatusCode.NotFound, firstAsset.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, secondAsset.StatusCode);

            await using var verificationScope = _factory.Services.CreateAsyncScope();
            var dbContext =
                verificationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var persistedJob = await dbContext.AssetImportJobs
                .AsNoTracking()
                .SingleAsync(item => item.Id == job.Id);
            Assert.Equal(
                Inventory.Domain.Importing.AssetImportStatus.ReadyToCommit,
                persistedJob.Status);
            Assert.False(
                await dbContext.AssetCategories
                    .AsNoTracking()
                    .AnyAsync(category => category.CodeNormalized == categoryCode));
            Assert.False(
                await dbContext.Locations
                    .AsNoTracking()
                    .AnyAsync(location => location.CodeNormalized == locationCode));

            var audits = await dbContext.AuditLogs
                .AsNoTracking()
                .Where(
                    audit =>
                        audit.TargetType == "asset_import_job" &&
                        audit.TargetId == job.Id.ToString())
                .Select(audit => new { audit.Action, audit.Outcome, audit.DetailsJson })
                .ToArrayAsync();
            Assert.DoesNotContain(
                audits,
                audit => audit.Action == "assets.import.committed");
            var rejection = Assert.Single(
                audits,
                audit => audit.Action == "assets.import.commit_rejected");
            Assert.Equal("rejected", rejection.Outcome);
            Assert.Contains(
                "late_persistence_conflict",
                rejection.DetailsJson,
                StringComparison.Ordinal);
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var dropTriggerSql = $"""DROP TRIGGER IF EXISTS "{triggerName}";""";
            await dbContext.Database.ExecuteSqlRawAsync(dropTriggerSql);
        }
    }

    [Fact]
    public async Task UpsertTransfersCustodyAndUpdatesPrimaryWarrantyAtomically()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var category = await CreateCategoryAsync(client, suffix);
        var location = await CreateLocationAsync(client, suffix);
        var person = await CreatePersonAsync(client, suffix);
        var department = await CreateDepartmentAsync(client, suffix);
        var asset = await CreateAssetAsync(client, category.Id, suffix);

        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = asset.Version,
            },
            ApiTestClient.JsonOptions);
        assignmentResponse.EnsureSuccessStatusCode();

        using var warrantyResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/warranties",
            new CreateWarrantyRequest
            {
                Provider = "Old Provider",
                CoverageType = "Manufacturer",
                StartDate = new DateOnly(2025, 1, 1),
                EndDate = new DateOnly(2027, 1, 1),
                Reference = "OLD",
                IsPrimary = true,
            },
            ApiTestClient.JsonOptions);
        warrantyResponse.EnsureSuccessStatusCode();

        var csv = BuildCsv(
            [
                Row(
                    asset.AssetTag,
                    category.Code,
                    manufacturer: "Updated Manufacturer",
                    model: "Updated Model",
                    serialNumber: asset.SerialNumber,
                    status: "Assigned",
                    locationCode: location.Code,
                    departmentCode: department.Code,
                    warrantyStartDate: "2026-01-01",
                    warrantyEndDate: "2030-01-01",
                    warrantyProvider: "New Provider",
                    warrantyReference: "NEW"),
            ]);
        using var validateResponse = await UploadAsync(
            client,
            csv,
            AssetImportModeContract.UpsertByAssetTag);
        validateResponse.EnsureSuccessStatusCode();
        var job = await validateResponse.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(1, job.UpdateRows);

        using var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();

        var updated = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Model", updated.Model);
        Assert.Equal(department.Id, updated.CurrentAssignment?.Target.Id);
        Assert.Equal("New Provider", updated.WarrantySummary.Provider);

        var assignments = await client.GetFromJsonAsync<PagedResponse<AssetAssignmentResponse>>(
            $"/api/v1/assets/{asset.Id}/assignments",
            ApiTestClient.JsonOptions);
        Assert.Equal(2, assignments?.TotalCount);
        Assert.Single(assignments?.Items ?? [], item => item.ReturnedAtUtc is null);
        Assert.Single(assignments?.Items ?? [], item => item.ReturnedAtUtc is not null);
    }

    [Fact]
    public async Task ImportAndExportEndpointsEnforceTheirNamedPermissions()
    {
        using var anonymous = _factory.CreateHttpsClient();
        using var anonymousExport = await anonymous.GetAsync("/api/v1/exports/assets.csv");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousExport.StatusCode);

        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var auditorExport = await auditor.GetAsync("/api/v1/exports/assets.csv");
        auditorExport.EnsureSuccessStatusCode();
        using var auditorImport = await UploadAsync(
            auditor,
            BuildCsv([Row($"AUD-{NewSuffix()}", "UNKNOWN")]),
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Forbidden, auditorImport.StatusCode);

        using var technician = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        using var technicianExport = await technician.GetAsync(
            "/api/v1/exports/assets.csv");
        Assert.Equal(HttpStatusCode.Forbidden, technicianExport.StatusCode);
        using var technicianImport = await UploadAsync(
            technician,
            BuildCsv([Row($"TECH-{NewSuffix()}", "UNKNOWN")]),
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Forbidden, technicianImport.StatusCode);
    }

    [Fact]
    public async Task UploadTypeSizeAndRowLimitsAreEnforcedBeforeInventoryChanges()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();

        using var wrongType = await UploadAsync(
            client,
            BuildCsv([Row($"TYPE-{suffix}", "UNKNOWN")]),
            AssetImportModeContract.CreateOnly,
            contentType: "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);

        using var tooLarge = await UploadAsync(
            client,
            new byte[(5 * 1024 * 1024) + 1],
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);

        var tooManyRows = Enumerable.Range(1, 501)
            .Select(
                index => Row(
                    $"ROW-{suffix}-{index}",
                    "UNKNOWN",
                    serialNumber: $"ROW-SER-{suffix}-{index}"))
            .ToArray();
        using var rowLimit = await UploadAsync(
            client,
            BuildCsv(tooManyRows),
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.BadRequest, rowLimit.StatusCode);
    }

    [Fact]
    public async Task ValidationRequiresAnExplicitImportMode()
    {
        using var client = await CreateAdministratorClientAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(
            BuildCsv([Row($"MODE-{NewSuffix()}", "UNKNOWN")]));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "assets.csv");

        using var response = await client.PostAsync(
            "/api/v1/imports/assets/validate",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpClient> CreateAdministratorClientAsync() =>
        _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] csv,
        AssetImportModeContract mode,
        string contentType = "text/csv")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "assets.csv");
        form.Add(new StringContent(mode.ToString()), "mode");
        return await client.PostAsync("/api/v1/imports/assets/validate", form);
    }

    private static byte[] BuildCsv(IReadOnlyList<IReadOnlyList<string?>> rows) =>
        AssetCsvCodec.Write(AssetCsvSchema.Headers, rows);

    private static IReadOnlyList<string?> Row(
        string assetTag,
        string category,
        string? deviceName = null,
        string? description = null,
        string? manufacturer = "Dell",
        string? model = "Latitude",
        string? serialNumber = null,
        string? lanMacAddress = null,
        string? biosGuid = null,
        string? ownership = null,
        string status = "InStock",
        string condition = "Good",
        string? locationCode = null,
        string? departmentCode = null,
        string? assignedPersonExternalId = null,
        string? purchaseDate = null,
        string? purchaseCost = null,
        string? currency = null,
        string? supplier = "Integration Supplier",
        string? warrantyStartDate = null,
        string? warrantyEndDate = null,
        string? warrantyProvider = null,
        string? warrantyReference = null,
        string? operatingSystem = null,
        string? operatingSystemVersion = null,
        string? windows11Compliant = null,
        string? legacyId = null,
        string? notes = null) =>
    [
        "2",
        assetTag,
        deviceName,
        category,
        description,
        manufacturer,
        model,
        serialNumber,
        lanMacAddress,
        biosGuid,
        ownership,
        status,
        condition,
        locationCode,
        departmentCode,
        assignedPersonExternalId,
        purchaseDate,
        purchaseCost,
        currency,
        supplier,
        warrantyStartDate,
        warrantyEndDate,
        warrantyProvider,
        warrantyReference,
        operatingSystem,
        operatingSystemVersion,
        windows11Compliant,
        legacyId,
        notes,
    ];

    private static IReadOnlyList<string?> LegacyRow(
        string deviceName,
        string assetType,
        string? description = null,
        string? serialNumber = null,
        string? lanMacAddress = null,
        string? biosGuid = null,
        string? usedBy = null,
        string school = "STR",
        string? location = null,
        string? product = null,
        string? vendor = null,
        string? ownership = null,
        string? cost = null,
        string warranty = "Yes",
        string? acquisitionDate = null,
        string? warrantyExpiryDate = null,
        string assetState = "In Use",
        string? operatingSystem = null,
        string? operatingSystemVersion = null,
        string? windows11Compliant = null) =>
    [
        deviceName,
        assetType,
        description,
        serialNumber,
        lanMacAddress,
        biosGuid,
        usedBy,
        school,
        location,
        product,
        vendor,
        ownership,
        cost,
        warranty,
        acquisitionDate,
        warrantyExpiryDate,
        assetState,
        operatingSystem,
        operatingSystemVersion,
        windows11Compliant,
    ];

    private static IReadOnlyList<string?> Version1Row(
        string assetTag,
        string category,
        string? serialNumber = null) =>
    [
        "1",
        assetTag,
        category,
        "Dell",
        "Latitude",
        serialNumber,
        "InStock",
        "Good",
        null,
        null,
        null,
        null,
        null,
        null,
        "Integration Supplier",
        null,
        null,
        null,
        null,
        null,
        null,
    ];

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"CAT-{suffix}",
                Name = $"Category {suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Category response was empty.");
    }

    private static async Task<LocationResponse> CreateLocationAsync(
        HttpClient client,
        string suffix)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new CreateLocationRequest
            {
                Code = $"LOC-{suffix}",
                Name = $"Location {suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Location response was empty.");
    }

    private static async Task<PersonResponse> CreatePersonAsync(
        HttpClient client,
        string suffix,
        string? externalId = null,
        string? displayName = null)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            new CreatePersonRequest
            {
                ExternalId = externalId ?? $"PER-{suffix}",
                DisplayName = displayName ?? $"Person {suffix}",
                Type = PersonTypeContract.Staff,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PersonResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Person response was empty.");
    }

    private static async Task<DepartmentResponse> CreateDepartmentAsync(
        HttpClient client,
        string suffix)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new CreateDepartmentRequest
            {
                Code = $"DEP-{suffix}",
                Name = $"Department {suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DepartmentResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Department response was empty.");
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        Guid categoryId,
        string suffix)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"UPSERT-{suffix}",
                CategoryId = categoryId,
                Manufacturer = "Original Manufacturer",
                Model = "Original Model",
                SerialNumber = $"UPSERT-SER-{suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Asset response was empty.");
    }

    private async Task<IReadOnlyList<string>> GetAuditActionsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs
            .AsNoTracking()
            .OrderBy(audit => audit.OccurredAtUtc)
            .Select(audit => audit.Action)
            .ToArrayAsync();
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..10];
}
