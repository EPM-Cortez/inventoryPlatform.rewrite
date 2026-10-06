using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Attachments;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class CustodyDocumentsApiTests : IClassFixture<InventoryApiFactory>
{
    private static readonly byte[] PdfContent = "%PDF-test invoice"u8.ToArray();

    private static readonly byte[] PngContent =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    private readonly InventoryApiFactory _factory;

    public CustodyDocumentsApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LeaverAndOverdueWorkflowNeverAutoReturnsCustody()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var asset = await CreateAssetAsync(client, category.Id, $"LEV-{suffix}");
        var person = await CreatePersonAsync(client, $"STF-{suffix}");
        var expectedReturnOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

        var assignResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = asset.Version,
                ExpectedReturnOn = expectedReturnOn,
            },
            ApiTestClient.JsonOptions);
        assignResponse.EnsureSuccessStatusCode();
        var assignment = await assignResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(assignment);
        Assert.Equal(expectedReturnOn, assignment.Assignment.ExpectedReturnOn);
        Assert.True(assignment.Assignment.IsOverdue);

        var overdue = await client.GetFromJsonAsync<PagedResponse<OverdueLoanResponse>>(
            $"/api/v1/assignments/overdue?personId={person.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(asset.Id, Assert.Single(overdue?.Items ?? []).AssetId);

        var createLeaverResponse = await client.PostAsJsonAsync(
            "/api/v1/leavers",
            new CreateLeaverCaseRequest
            {
                PersonId = person.Id,
                LeavingOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                Notes = "Collect all equipment.",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createLeaverResponse.StatusCode);
        var leaver = await createLeaverResponse.Content
            .ReadFromJsonAsync<LeaverCaseResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(leaver);
        Assert.Equal(asset.Id, Assert.Single(leaver.CurrentAssets).AssetId);

        var blockedCompletion = await client.PostAsJsonAsync(
            $"/api/v1/leavers/{leaver.Id}/complete",
            new ChangeLeaverCaseStatusRequest { Version = leaver.Version },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blockedCompletion.StatusCode);

        var returnResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments/" +
            $"{assignment.Assignment.Id}/return",
            new ReturnAssetRequest
            {
                AssetVersion = assignment.AssetVersion,
                AssignmentVersion = assignment.Assignment.Version,
            },
            ApiTestClient.JsonOptions);
        returnResponse.EnsureSuccessStatusCode();
        var returned = await returnResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(returned);

        var completedResponse = await client.PostAsJsonAsync(
            $"/api/v1/leavers/{leaver.Id}/complete",
            new ChangeLeaverCaseStatusRequest { Version = leaver.Version },
            ApiTestClient.JsonOptions);
        completedResponse.EnsureSuccessStatusCode();
        var completed = await completedResponse.Content
            .ReadFromJsonAsync<LeaverCaseResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(completed);
        Assert.Equal(LeaverCaseStatusContract.Completed, completed.Status);
        Assert.Empty(completed.CurrentAssets);

        var blockedReassignment = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = returned.AssetVersion,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blockedReassignment.StatusCode);

        var rehireResponse = await client.PostAsJsonAsync(
            $"/api/v1/leavers/{completed.Id}/rehire",
            new ChangeLeaverCaseStatusRequest { Version = completed.Version },
            ApiTestClient.JsonOptions);
        rehireResponse.EnsureSuccessStatusCode();
        var rehired = await rehireResponse.Content
            .ReadFromJsonAsync<LeaverCaseResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(rehired);
        Assert.Equal(LeaverCaseStatusContract.Rehired, rehired.Status);

        var reassignment = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = returned.AssetVersion,
            },
            ApiTestClient.JsonOptions);
        reassignment.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AttachmentsAreMagicCheckedDownloadedAsAttachmentsAndArchived()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var asset = await CreateAssetAsync(client, category.Id, $"ATT-{suffix}");

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("Invoice"), "category");
        var document = new ByteArrayContent(PdfContent);
        document.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(document, "file", "invoice.pdf");
        var uploadResponse = await client.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            upload);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var attachment = await uploadResponse.Content
            .ReadFromJsonAsync<AssetAttachmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(attachment);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(64, attachment.Sha256.Length);

        var download = await client.GetAsync(
            $"/api/v1/assets/{asset.Id}/attachments/{attachment.Id}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains(
            "no-store",
            download.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-cache",
            download.Headers.Pragma.Single().Name);
        Assert.Equal(
            PdfContent,
            await download.Content.ReadAsByteArrayAsync());

        var archive = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/attachments/{attachment.Id}/archive",
            new ArchiveAssetAttachmentRequest { Version = attachment.Version },
            ApiTestClient.JsonOptions);
        archive.EnsureSuccessStatusCode();

        var active = await client.GetFromJsonAsync<PagedResponse<AssetAttachmentResponse>>(
            $"/api/v1/assets/{asset.Id}/attachments",
            ApiTestClient.JsonOptions);
        Assert.Empty(active?.Items ?? []);
        var all = await client.GetFromJsonAsync<PagedResponse<AssetAttachmentResponse>>(
            $"/api/v1/assets/{asset.Id}/attachments?includeArchived=true",
            ApiTestClient.JsonOptions);
        Assert.NotNull(Assert.Single(all?.Items ?? []).ArchivedAtUtc);

        using var invalidUpload = new MultipartFormDataContent();
        invalidUpload.Add(new StringContent("Photo"), "category");
        invalidUpload.Add(
            new ByteArrayContent(PngContent),
            "file",
            "wrong.pdf");
        var invalid = await client.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            invalidUpload);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task CorruptAttachmentContentIsNotServedAndFailureIsAudited()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var asset = await CreateAssetAsync(client, category.Id, $"HASH-{suffix}");

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("DisposalCertificate"), "category");
        upload.Add(new ByteArrayContent(PdfContent), "file", "certificate.pdf");
        var uploadResponse = await client.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            upload);
        uploadResponse.EnsureSuccessStatusCode();
        var attachment = await uploadResponse.Content
            .ReadFromJsonAsync<AssetAttachmentResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(attachment);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();
            var tampered = Enumerable.Repeat(
                    (byte)'X',
                    checked((int)attachment.SizeBytes))
                .ToArray();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "AssetAttachments"
                SET "Content" = {tampered}
                WHERE "Id" = {attachment.Id};
                """);
        }

        var download = await client.GetAsync(
            $"/api/v1/assets/{asset.Id}/attachments/{attachment.Id}/download");
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider
            .GetRequiredService<InventoryDbContext>();
        Assert.True(
            await verification.AuditLogs
                .AsNoTracking()
                .AnyAsync(
                    audit =>
                        audit.Action == "attachment.downloaded" &&
                        audit.TargetId == attachment.Id.ToString() &&
                        audit.Outcome == "failed"));
    }

    [Fact]
    public async Task MalwareAndUnavailableScannerBothFailClosedAndAreAudited()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            attachmentMalwareScanner: new RejectThenUnavailableScanner());
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var asset = await CreateAssetAsync(client, category.Id, $"SCAN-{suffix}");

        var eicarPdf = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-\nX5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-" +
            "STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        using var rejectedUpload = new MultipartFormDataContent();
        rejectedUpload.Add(new StringContent("Invoice"), "category");
        rejectedUpload.Add(
            new ByteArrayContent(eicarPdf),
            "file",
            "eicar-test.pdf");
        var rejected = await client.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            rejectedUpload);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        using var unavailableUpload = new MultipartFormDataContent();
        unavailableUpload.Add(
            new StringContent("WarrantyDocument"),
            "category");
        unavailableUpload.Add(
            new ByteArrayContent(PdfContent),
            "file",
            "warranty.pdf");
        var unavailable = await client.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            unavailableUpload);
        Assert.True(
            unavailable.StatusCode == HttpStatusCode.Conflict,
            await unavailable.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.False(
            await dbContext.AssetAttachments
                .AsNoTracking()
                .AnyAsync(attachment => attachment.AssetId == asset.Id));
        Assert.Equal(
            2,
            await dbContext.AuditLogs
                .AsNoTracking()
                .CountAsync(
                    audit =>
                        audit.Action == "attachment.scanned" &&
                        audit.Outcome == "failed"));
    }

    [Fact]
    public async Task SavedSearchesArePersonalAndHaveOneDefault()
    {
        using var administrator = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var request = new CreateSavedAssetSearchRequest
        {
            Name = $"My laptops {Guid.NewGuid():N}",
            SchemaVersion = 1,
            Filter = new SavedAssetFilterRequest
            {
                Query = "laptop",
                Assigned = false,
                PageSize = 25,
                Sort = "-updatedAtUtc",
            },
        };
        var create = await administrator.PostAsJsonAsync(
            "/api/v1/me/saved-asset-searches",
            request,
            ApiTestClient.JsonOptions);
        Assert.True(
            create.StatusCode == HttpStatusCode.Created,
            await create.Content.ReadAsStringAsync());
        var saved = await create.Content
            .ReadFromJsonAsync<SavedAssetSearchResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(saved);

        var setDefault = await administrator.PostAsJsonAsync(
            $"/api/v1/me/saved-asset-searches/{saved.Id}/default",
            new ChangeSavedAssetSearchRequest { Version = saved.Version },
            ApiTestClient.JsonOptions);
        setDefault.EnsureSuccessStatusCode();
        var currentDefault = await administrator
            .GetFromJsonAsync<SavedAssetSearchResponse>(
                "/api/v1/me/saved-asset-searches/default",
                ApiTestClient.JsonOptions);
        Assert.Equal(saved.Id, currentDefault?.Id);

        var secondCreate = await administrator.PostAsJsonAsync(
            "/api/v1/me/saved-asset-searches",
            new CreateSavedAssetSearchRequest
            {
                Name = $"My desktops {Guid.NewGuid():N}",
                SchemaVersion = request.SchemaVersion,
                Filter = request.Filter,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, secondCreate.StatusCode);
        var second = await secondCreate.Content
            .ReadFromJsonAsync<SavedAssetSearchResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(second);

        var switchToSecond = await administrator.PostAsJsonAsync(
            $"/api/v1/me/saved-asset-searches/{second.Id}/default",
            new ChangeSavedAssetSearchRequest { Version = second.Version },
            ApiTestClient.JsonOptions);
        switchToSecond.EnsureSuccessStatusCode();
        var secondDefault = await switchToSecond.Content
            .ReadFromJsonAsync<SavedAssetSearchResponse>(ApiTestClient.JsonOptions);
        Assert.True(secondDefault?.IsDefault);

        var searches = await administrator
            .GetFromJsonAsync<PagedResponse<SavedAssetSearchResponse>>(
                "/api/v1/me/saved-asset-searches?pageSize=50",
                ApiTestClient.JsonOptions);
        var refreshedFirst = Assert.Single(
            searches?.Items ?? [],
            item => item.Id == saved.Id);
        Assert.False(refreshedFirst.IsDefault);
        var switchBack = await administrator.PostAsJsonAsync(
            $"/api/v1/me/saved-asset-searches/{refreshedFirst.Id}/default",
            new ChangeSavedAssetSearchRequest { Version = refreshedFirst.Version },
            ApiTestClient.JsonOptions);
        switchBack.EnsureSuccessStatusCode();
        var firstDefaultAgain = await switchBack.Content
            .ReadFromJsonAsync<SavedAssetSearchResponse>(ApiTestClient.JsonOptions);
        Assert.True(firstDefaultAgain?.IsDefault);

        using var technician = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var crossUserRead = await technician.GetAsync(
            $"/api/v1/me/saved-asset-searches/{saved.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossUserRead.StatusCode);
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"CD-{suffix}",
                Name = $"Category {suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions))!;
    }

    private static async Task<PersonResponse> CreatePersonAsync(
        HttpClient client,
        string externalId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            new CreatePersonRequest
            {
                ExternalId = externalId,
                DisplayName = $"Leaver {externalId}",
                Type = PersonTypeContract.Staff,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonResponse>(
            ApiTestClient.JsonOptions))!;
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        Guid categoryId,
        string assetTag)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = assetTag,
                DeviceName = $"Device {assetTag}",
                CategoryId = categoryId,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions))!;
    }

    private sealed class RejectThenUnavailableScanner
        : IAttachmentMalwareScanner
    {
        private int _scanCount;

        public Task<AttachmentScannerReadiness> CheckReadinessAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new AttachmentScannerReadiness(
                    false,
                    "Integration test scanner",
                    "scanner_unavailable"));

        public Task<AttachmentScanResult> ScanAsync(
            string fileName,
            string contentType,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _scanCount);
            return Task.FromResult(
                count == 1
                    ? new AttachmentScanResult(
                        AttachmentScanVerdict.Rejected,
                        "Integration test scanner",
                        "eicar_test_signature")
                    : new AttachmentScanResult(
                        AttachmentScanVerdict.Unavailable,
                        "Integration test scanner",
                        "scanner_unavailable"));
        }
    }
}
