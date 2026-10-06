using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.DataExchange;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class LegacyReconciliationApiTests
{
    [Fact]
    public async Task DryRunsAreRepeatableAndSignedExceptionsCannotBypassCommitGate()
    {
        using var factory = new InventoryApiFactory();
        using var administrator =
            await factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var technician =
            await factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var duplicateTag = $"RECON-DUP-{suffix}";
        var csv = BuildCsv(
            [
                Row(
                    duplicateTag,
                    $"Reconciliation Category {suffix}",
                    $"SER-A-{suffix}"),
                Row(
                    duplicateTag,
                    $"Reconciliation Category {suffix}",
                    $"SER-B-{suffix}"),
                Row(
                    $"RECON-UNASSIGNED-{suffix}",
                    $"Reconciliation Category {suffix}",
                    $"SER-C-{suffix}",
                    assignedPersonExternalId:
                        $"departed-{suffix}"),
            ]);

        using var forbidden = await UploadAsync(
            technician,
            csv,
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var firstResponse = await UploadAsync(
            administrator,
            csv,
            AssetImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content
            .ReadFromJsonAsync<LegacyReconciliationResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(first);
        Assert.Equal(
            AssetImportStatusContract.ValidationFailed,
            first.Report.ImportStatus);
        Assert.Equal(3, first.Report.Counts.SourceRows);
        Assert.Equal(2, first.Report.Counts.ErrorRows);
        Assert.True(first.Report.DuplicateIssueCount >= 2);
        Assert.True(first.Report.UnmappedReferenceIssueCount >= 1);
        Assert.Contains(
            first.Report.Issues,
            issue =>
                issue.Code ==
                "unmapped_assignment_imported_unassigned");
        Assert.False(first.CutoverReady);

        using var secondResponse = await UploadAsync(
            administrator,
            csv,
            AssetImportModeContract.CreateOnly);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content
            .ReadFromJsonAsync<LegacyReconciliationResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(second);
        Assert.NotEqual(
            first.Report.ImportJobId,
            second.Report.ImportJobId);
        Assert.Equal(
            first.Report.ReportFingerprint,
            second.Report.ReportFingerprint);
        Assert.Equal(
            first.Report.Issues.Select(issue => issue.IssueKey),
            second.Report.Issues.Select(issue => issue.IssueKey));

        var missingAcknowledgements =
            await administrator.PostAsJsonAsync(
                $"/api/v1/legacy-reconciliation/{first.Report.ImportJobId}/sign-off",
                new SignOffLegacyReconciliationRequest
                {
                    Decision =
                        LegacyReconciliationDecisionContract.Approved,
                    ReportFingerprint =
                        first.Report.ReportFingerprint,
                    Notes =
                        "Reviewed but intentionally missing required exception acknowledgements.",
                },
                ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            missingAcknowledgements.StatusCode);

        var blockingIssues = first.Report.Issues
            .Where(issue => issue.Severity == "blocking")
            .Select(
                issue =>
                    new LegacyExceptionAcknowledgementRequest
                    {
                        IssueKey = issue.IssueKey,
                        Rationale =
                            "Source row excluded pending correction and recorded for reconciliation.",
                    })
            .ToArray();
        Assert.NotEmpty(blockingIssues);
        var signOffResponse =
            await administrator.PostAsJsonAsync(
                $"/api/v1/legacy-reconciliation/{first.Report.ImportJobId}/sign-off",
                new SignOffLegacyReconciliationRequest
                {
                    Decision =
                        LegacyReconciliationDecisionContract.Approved,
                    ReportFingerprint =
                        first.Report.ReportFingerprint,
                    Notes =
                        "Dry-run exceptions reviewed; this is evidence only and not cutover approval.",
                    Exceptions = blockingIssues,
                },
                ApiTestClient.JsonOptions);
        signOffResponse.EnsureSuccessStatusCode();
        var signed = await signOffResponse.Content
            .ReadFromJsonAsync<LegacyReconciliationResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(signed);
        Assert.NotNull(signed.SignOff);
        Assert.Equal(
            blockingIssues.Length,
            signed.SignOff.SignedExceptions.Count);
        Assert.False(signed.CutoverReady);

        var secondSignOff =
            await administrator.PostAsJsonAsync(
                $"/api/v1/legacy-reconciliation/{first.Report.ImportJobId}/sign-off",
                new SignOffLegacyReconciliationRequest
                {
                    Decision =
                        LegacyReconciliationDecisionContract.Rejected,
                    ReportFingerprint =
                        first.Report.ReportFingerprint,
                    Notes = "An immutable second decision must be rejected.",
                },
                ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.Conflict,
            secondSignOff.StatusCode);

        using var packageResponse = await administrator.GetAsync(
            $"/api/v1/legacy-reconciliation/{first.Report.ImportJobId}/cutover-package");
        packageResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            "attachment",
            packageResponse.Content.Headers.ContentDisposition?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var package = await packageResponse.Content
            .ReadFromJsonAsync<LegacyCutoverPackageResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(package);
        Assert.False(package.CutoverReady);
        Assert.Contains(
            package.BlockingReasons,
            reason => reason.Contains(
                "validation errors",
                StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(package.RollbackProcedure);
        Assert.Matches(
            "^[A-F0-9]{64}$",
            package.PackageFingerprint);

        Assert.Equal(
            2,
            await factory.CountAuditActionsAsync(
                "legacy.reconciliation.dry_run"));
        Assert.Equal(
            1,
            await factory.CountAuditActionsAsync(
                "legacy.reconciliation.signed"));
    }

    [Fact]
    public async Task CleanApprovedDryRunProducesReadyCutoverPackage()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            utcNow: new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        using var administrator =
            await factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var csv = BuildCsv(
            [
                Row(
                    $"RECON-CLEAN-{suffix}",
                    $"New Reconciliation Category {suffix}",
                    $"RECON-CLEAN-SERIAL-{suffix}"),
            ]);
        using var dryRunResponse = await UploadAsync(
            administrator,
            csv,
            AssetImportModeContract.CreateOnly);
        dryRunResponse.EnsureSuccessStatusCode();
        var dryRun = await dryRunResponse.Content
            .ReadFromJsonAsync<LegacyReconciliationResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(dryRun);
        Assert.Equal(
            AssetImportStatusContract.ReadyToCommit,
            dryRun.Report.ImportStatus);
        Assert.Equal(0, dryRun.Report.Counts.ErrorRows);
        Assert.Equal(1, dryRun.Report.Mappings.PlannedCategoryCreates);

        var signOffResponse =
            await administrator.PostAsJsonAsync(
                $"/api/v1/legacy-reconciliation/{dryRun.Report.ImportJobId}/sign-off",
                new SignOffLegacyReconciliationRequest
                {
                    Decision =
                        LegacyReconciliationDecisionContract.Approved,
                    ReportFingerprint =
                        dryRun.Report.ReportFingerprint,
                    Notes =
                        "Counts, mapping decisions, sample comparison, and rollback procedure approved.",
                },
                ApiTestClient.JsonOptions);
        signOffResponse.EnsureSuccessStatusCode();
        var signed = await signOffResponse.Content
            .ReadFromJsonAsync<LegacyReconciliationResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(signed);
        Assert.True(signed.CutoverReady);

        var package = await administrator.GetFromJsonAsync<
            LegacyCutoverPackageResponse>(
            $"/api/v1/legacy-reconciliation/{dryRun.Report.ImportJobId}/cutover-package",
            ApiTestClient.JsonOptions);
        Assert.NotNull(package);
        Assert.True(package.CutoverReady);
        Assert.Empty(package.BlockingReasons);
        Assert.Equal(
            dryRun.Report.Counts.ExpectedTargetAssetsAfter,
            package.Counts.ExpectedTargetAssetsAfter);
        Assert.NotEmpty(package.CutoverProcedure);
        Assert.NotEmpty(package.RollbackProcedure);

        factory.AdvanceClock(TimeSpan.FromDays(8));
        var expiredView = await administrator.GetFromJsonAsync<
            LegacyReconciliationResponse>(
            $"/api/v1/legacy-reconciliation/{dryRun.Report.ImportJobId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(expiredView);
        Assert.False(expiredView.CutoverReady);
        Assert.NotNull(expiredView.SignOff);

        var expiredPackage = await administrator.GetFromJsonAsync<
            LegacyCutoverPackageResponse>(
            $"/api/v1/legacy-reconciliation/{dryRun.Report.ImportJobId}/cutover-package",
            ApiTestClient.JsonOptions);
        Assert.NotNull(expiredPackage);
        Assert.False(expiredPackage.CutoverReady);
        Assert.Contains(
            expiredPackage.BlockingReasons,
            reason => reason.Contains("expired", StringComparison.OrdinalIgnoreCase));
        using var expiredCommit = await administrator.PostAsJsonAsync(
            $"/api/v1/imports/{dryRun.Report.ImportJobId}/commit",
            new CommitAssetImportRequest { Version = package.ImportJobVersion },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, expiredCommit.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var budget = scope.ServiceProvider.GetRequiredService<EfImportPreviewBudget>();
        Assert.Equal(0, await budget.PurgeExpiredAsync(CancellationToken.None));
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.NotEqual("{}", (await database.LegacyReconciliationRuns.SingleAsync()).ReportJson);
        Assert.NotEqual("[]", (await database.AssetImportJobs.SingleAsync()).ValidatedRowsJson);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] content,
        AssetImportModeContract mode)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "legacy-reconciliation.csv");
        form.Add(
            new StringContent(mode.ToString()),
            "mode");
        return await client.PostAsync(
            "/api/v1/legacy-reconciliation/dry-runs",
            form);
    }

    private static byte[] BuildCsv(
        IReadOnlyList<IReadOnlyList<string?>> rows) =>
        AssetCsvCodec.Write(AssetCsvSchema.Headers, rows);

    private static IReadOnlyList<string?> Row(
        string assetTag,
        string category,
        string serialNumber,
        string? assignedPersonExternalId = null) =>
    [
        "2",
        assetTag,
        assetTag,
        category,
        "Legacy reconciliation fixture",
        "Contoso",
        "Migration Device",
        serialNumber,
        null,
        null,
        "Owned",
        assignedPersonExternalId is null ? "InStock" : "Assigned",
        "Good",
        null,
        null,
        assignedPersonExternalId,
        "2026",
        "100.00",
        "GBP",
        "Migration Supplier",
        "2026",
        "2029",
        "Migration Warranty",
        null,
        "Windows",
        "11",
        "true",
        $"LEGACY-{assetTag}",
        "Migration dry run",
    ];
}
