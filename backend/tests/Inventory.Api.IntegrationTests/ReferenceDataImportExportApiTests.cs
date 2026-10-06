using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.DataExchange;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class ReferenceDataImportExportApiTests :
    IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ReferenceDataImportExportApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SuppliedCompatibilityFormatsCommitExportAndRoundTrip()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var staffKey = $"staff{suffix}".ToLowerInvariant();
        var staffEmail = $"{staffKey}@example.test";
        var staffCsv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.StaffCompatibilityHeaders,
            [["Reference Staff", "Staff", staffEmail]]);

        var staffJob = await ValidateAsync(
            client,
            "/api/v1/imports/people/validate",
            "staff.csv",
            staffCsv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ReadyToCommit,
            staffJob.Status);
        Assert.Equal(ReferenceDataImportResourceContract.People, staffJob.Resource);
        var staffPreview = Assert.Single(staffJob.Preview);
        Assert.Equal(staffKey, staffPreview.Key);
        Assert.True(staffPreview.KeyGenerated);
        await CommitAsync(client, staffJob);

        var departmentCsv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.DepartmentCompatibilityHeaders,
            [["Sixth Form", null]]);
        var departmentJob = await ValidateAsync(
            client,
            "/api/v1/imports/departments/validate",
            "departments.csv",
            departmentCsv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ReadyToCommit,
            departmentJob.Status);
        var departmentPreview = Assert.Single(departmentJob.Preview);
        Assert.Equal("SIXTH-FORM", departmentPreview.Key);
        Assert.True(departmentPreview.KeyGenerated);
        await CommitAsync(client, departmentJob);

        var locationCode = $"R{suffix}";
        var locationCsv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.LocationCompatibilityHeaders,
            [["Reference Room", "Room", locationCode]]);
        var locationJob = await ValidateAsync(
            client,
            "/api/v1/imports/locations/validate",
            "location.csv",
            locationCsv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ReadyToCommit,
            locationJob.Status);
        var locationPreview = Assert.Single(locationJob.Preview);
        Assert.Equal(locationCode, locationPreview.Key);
        Assert.Equal("Room", locationPreview.Type);
        Assert.Null(locationPreview.ParentKey);
        await CommitAsync(client, locationJob);

        var peopleExport = await ExportAsync(client, "/api/v1/exports/people.csv");
        Assert.Equal(ReferenceDataCsvSchemas.PeopleHeaders, peopleExport.Records[0]);
        Assert.Contains(
            peopleExport.Records.Skip(1),
            row => Unprotect(row[1]) == staffKey &&
                   Unprotect(row[4]) == staffEmail);

        var departmentExport = await ExportAsync(
            client,
            "/api/v1/exports/departments.csv");
        Assert.Equal(
            ReferenceDataCsvSchemas.DepartmentHeaders,
            departmentExport.Records[0]);
        Assert.Contains(
            departmentExport.Records.Skip(1),
            row => Unprotect(row[1]) == "SIXTH-FORM" &&
                   Unprotect(row[2]) == "Sixth Form");

        var locationExport = await ExportAsync(
            client,
            "/api/v1/exports/locations.csv");
        Assert.Equal(
            ReferenceDataCsvSchemas.LocationHeaders,
            locationExport.Records[0]);
        Assert.Contains(
            locationExport.Records.Skip(1),
            row => Unprotect(row[1]) == locationCode &&
                   Unprotect(row[3]) == "Room" &&
                   string.IsNullOrEmpty(Unprotect(row[4])));

        await AssertRoundTripUnchangedAsync(
            client,
            "/api/v1/imports/people/validate",
            "people.csv",
            peopleExport.Content);
        await AssertRoundTripUnchangedAsync(
            client,
            "/api/v1/imports/departments/validate",
            "departments.csv",
            departmentExport.Content);
        await AssertRoundTripUnchangedAsync(
            client,
            "/api/v1/imports/locations/validate",
            "locations.csv",
            locationExport.Content);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var actions = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(
                audit =>
                    audit.Action == "reference_data.import.validated" ||
                    audit.Action == "reference_data.import.committed" ||
                    audit.Action == "reference_data.exported")
            .Select(audit => audit.Action)
            .ToArrayAsync();
        Assert.Contains("reference_data.import.validated", actions);
        Assert.Contains("reference_data.import.committed", actions);
        Assert.Contains("reference_data.exported", actions);
    }

    [Fact]
    public async Task ValidationReportsDuplicateEmailAndLocationCycleWithoutWritingData()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var sharedEmail = $"shared{suffix}@example.test";
        var peopleCsv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.PeopleHeaders,
            [
                ["1", $"P1-{suffix}", "First", "Staff", sharedEmail, "true"],
                ["1", $"P2-{suffix}", "Second", "Staff", sharedEmail, "true"],
            ]);
        var peopleJob = await ValidateAsync(
            client,
            "/api/v1/imports/people/validate",
            "people.csv",
            peopleCsv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ValidationFailed,
            peopleJob.Status);
        Assert.Equal(2, peopleJob.ErrorRows);
        Assert.All(
            peopleJob.Errors,
            error => Assert.Equal("duplicate_person_email_in_file", error.Code));

        var firstCode = $"CYCLE-A-{suffix}";
        var secondCode = $"CYCLE-B-{suffix}";
        var locationsCsv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.LocationHeaders,
            [
                ["1", firstCode, "Cycle A", "Room", secondCode, "true"],
                ["1", secondCode, "Cycle B", "Room", firstCode, "true"],
            ]);
        var locationJob = await ValidateAsync(
            client,
            "/api/v1/imports/locations/validate",
            "locations.csv",
            locationsCsv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ValidationFailed,
            locationJob.Status);
        Assert.Equal(2, locationJob.ErrorRows);
        Assert.All(
            locationJob.Errors,
            error => Assert.Equal("location_cycle", error.Code));

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.False(await dbContext.People.AnyAsync(
            person => person.Email == sharedEmail));
        Assert.False(await dbContext.Locations.AnyAsync(
            location =>
                location.CodeNormalized == firstCode ||
                location.CodeNormalized == secondCode));
    }

    [Fact]
    public async Task CommitRejectsStaleCreateAtomicallyAndAuditsTheRejection()
    {
        using var client = await CreateAdministratorClientAsync();
        var suffix = NewSuffix();
        var firstCode = $"ATOMIC-A-{suffix}";
        var conflictCode = $"ATOMIC-B-{suffix}";
        var csv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.DepartmentHeaders,
            [
                ["1", firstCode, "Atomic first", "true"],
                ["1", conflictCode, "Atomic conflict", "true"],
            ]);
        var job = await ValidateAsync(
            client,
            "/api/v1/imports/departments/validate",
            "departments.csv",
            csv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(
            ReferenceDataImportStatusContract.ReadyToCommit,
            job.Status);

        using var createConflict = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new CreateDepartmentRequest
            {
                Code = conflictCode,
                Name = "Created after validation",
            },
            ApiTestClient.JsonOptions);
        createConflict.EnsureSuccessStatusCode();

        using var commit = await client.PostAsJsonAsync(
            $"/api/v1/imports/reference-data/{job.Id}/commit",
            new CommitReferenceDataImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.False(await dbContext.Departments.AnyAsync(
            department => department.CodeNormalized == firstCode));
        Assert.True(await dbContext.Departments.AnyAsync(
            department => department.CodeNormalized == conflictCode));
        var storedJob = await dbContext.ReferenceDataImportJobs
            .AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(
            Inventory.Domain.Importing.ReferenceDataImportStatus.ReadyToCommit,
            storedJob.Status);
        Assert.True(await dbContext.AuditLogs.AnyAsync(
            audit =>
                audit.Action == "reference_data.import.commit_rejected" &&
                audit.TargetId == job.Id.ToString()));
    }

    [Fact]
    public async Task ReferenceDataImportsRequireAnExplicitMode()
    {
        using var client = await CreateAdministratorClientAsync();
        using var form = new MultipartFormDataContent();
        var csv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.StaffCompatibilityHeaders,
            [["Mode Test", "Staff", "mode@example.test"]]);
        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "staff.csv");

        using var response = await client.PostAsync(
            "/api/v1/imports/people/validate",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TechnicianAndAuditorCannotUseReferenceDataBulkExchange()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var csv = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.StaffCompatibilityHeaders,
            [["Forbidden Staff", "Staff", "forbidden@example.test"]]);

        using var import = await UploadAsync(
            client,
            "/api/v1/imports/people/validate",
            "staff.csv",
            csv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);

        foreach (var path in new[]
                 {
                     "/api/v1/exports/people.csv",
                     "/api/v1/exports/departments.csv",
                     "/api/v1/exports/locations.csv",
                 })
        {
            using var export = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        }

        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var auditorImport = await UploadAsync(
            auditor,
            "/api/v1/imports/people/validate",
            "staff.csv",
            csv,
            ReferenceDataImportModeContract.CreateOnly);
        Assert.Equal(HttpStatusCode.Forbidden, auditorImport.StatusCode);
        foreach (var path in new[]
                 {
                     "/api/v1/exports/people.csv",
                     "/api/v1/exports/departments.csv",
                     "/api/v1/exports/locations.csv",
                 })
        {
            using var export = await auditor.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        }
    }

    private Task<HttpClient> CreateAdministratorClientAsync() =>
        _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

    private static async Task<ReferenceDataImportJobResponse> ValidateAsync(
        HttpClient client,
        string path,
        string fileName,
        byte[] csv,
        ReferenceDataImportModeContract mode)
    {
        using var response = await UploadAsync(client, path, fileName, csv, mode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ReferenceDataImportJobResponse>(
                ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The API did not return a reference-data import job.");
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string path,
        string fileName,
        byte[] csv,
        ReferenceDataImportModeContract mode)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(mode.ToString()), "mode");
        return await client.PostAsync(path, form);
    }

    private static async Task<ReferenceDataImportCommitResponse> CommitAsync(
        HttpClient client,
        ReferenceDataImportJobResponse job)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/imports/reference-data/{job.Id}/commit",
            new CommitReferenceDataImportRequest { Version = job.Version },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<ReferenceDataImportCommitResponse>(
                ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException(
                "The API did not return a reference-data commit result.");
    }

    private static async Task<ExportedCsv> ExportAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            StringComparison.Ordinal);
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], content[..3]);
        var document = AssetCsvCodec.Parse(AssetCsvCodec.DecodeStrictUtf8(content));
        Assert.Equal(
            document.Records.Count - 1,
            int.Parse(
                Assert.Single(response.Headers.GetValues("X-Inventory-Row-Count")),
                System.Globalization.CultureInfo.InvariantCulture));
        return new ExportedCsv(content, document.Records);
    }

    private static async Task AssertRoundTripUnchangedAsync(
        HttpClient client,
        string path,
        string fileName,
        byte[] content)
    {
        var job = await ValidateAsync(
            client,
            path,
            fileName,
            content,
            ReferenceDataImportModeContract.UpsertByKey);
        Assert.Equal(
            ReferenceDataImportStatusContract.ReadyToCommit,
            job.Status);
        Assert.Equal(job.ValidRows, job.UpdateRows);
        var result = await CommitAsync(client, job);
        Assert.Equal(job.ValidRows, result.UnchangedRows);
        Assert.Equal(0, result.CreatedRows);
        Assert.Equal(0, result.UpdatedRows);
    }

    private static string Unprotect(string value) =>
        AssetCsvCodec.RemoveFormulaProtection(value);

    private static string NewSuffix() =>
        Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record ExportedCsv(
        byte[] Content,
        IReadOnlyList<IReadOnlyList<string>> Records);
}
