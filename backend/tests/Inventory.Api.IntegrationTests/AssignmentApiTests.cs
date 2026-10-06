using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class AssignmentApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AssignmentApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManualPeopleAndDepartmentMutationsHaveAtomicActorAudit()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var person = await CreatePersonAsync(client, $"AUD-{suffix}", "Audit Person");
        var department = await CreateDepartmentAsync(
            client,
            $"AUD-{suffix}",
            "Audit Department");

        var personUpdate = await client.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = person.ExternalId,
                DisplayName = "Renamed Audit Person",
                Type = person.Type,
                Email = person.Email,
                IsActive = true,
                Version = person.Version,
            },
            ApiTestClient.JsonOptions);
        personUpdate.EnsureSuccessStatusCode();
        var departmentUpdate = await client.PutAsJsonAsync(
            $"/api/v1/departments/{department.Id}",
            new UpdateDepartmentRequest
            {
                Code = department.Code,
                Name = "Renamed Audit Department",
                IsActive = true,
                Version = department.Version,
            },
            ApiTestClient.JsonOptions);
        departmentUpdate.EnsureSuccessStatusCode();

        var stale = await client.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = person.ExternalId,
                DisplayName = "Uncommitted stale edit",
                Type = person.Type,
                IsActive = true,
                Version = person.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(row =>
                (row.TargetType == "person" && row.TargetId == person.Id.ToString()) ||
                (row.TargetType == "department" && row.TargetId == department.Id.ToString()))
            .ToArrayAsync();
        Assert.Equal(4, audits.Length);
        Assert.Contains(audits, row => row.Action == "person.created");
        Assert.Contains(audits, row => row.Action == "person.updated");
        Assert.Contains(audits, row => row.Action == "department.created");
        Assert.Contains(audits, row => row.Action == "department.updated");
        Assert.All(audits, row => Assert.NotNull(row.ActorUserId));
        Assert.All(audits, row => Assert.Equal("succeeded", row.Outcome));
        Assert.DoesNotContain(audits, row =>
            row.DetailsJson?.Contains("Uncommitted stale edit", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AssignTransferAndReturnPreserveAtomicCustodyHistory()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var issueLocation = await CreateLocationAsync(
            client,
            $"ISS-{suffix}",
            "Issue desk");
        var returnLocation = await CreateLocationAsync(
            client,
            $"RET-{suffix}",
            "Returns store");
        var asset = await CreateAssetAsync(
            client,
            category.Id,
            issueLocation.Id,
            $"AST-{suffix}",
            AssetStatusContract.InStock);
        var person = await CreatePersonAsync(client, $"STF-{suffix}", "A. Patel");
        var department = await CreateDepartmentAsync(
            client,
            $"ICT-{suffix}",
            "ICT Services");

        var assignHttpResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = asset.Version,
                Notes = "Initial issue",
            },
            ApiTestClient.JsonOptions);
        assignHttpResponse.EnsureSuccessStatusCode();
        var assigned = await assignHttpResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(assigned);
        Assert.Equal(AssetStatusContract.Assigned, assigned.AssetStatus);
        Assert.Equal(AssignmentTargetTypeContract.Person, assigned.Assignment.Target.Type);
        Assert.Equal(issueLocation.Id, assigned.Assignment.AssignedLocation?.Id);
        Assert.NotEqual(asset.Version, assigned.AssetVersion);

        var assignedAsset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(person.Id, assignedAsset?.CurrentAssignment?.Target.Id);

        var personAssets = await client.GetFromJsonAsync<PagedResponse<HolderAssetResponse>>(
            $"/api/v1/people/{person.Id}/assets",
            ApiTestClient.JsonOptions);
        Assert.Equal(asset.Id, Assert.Single(personAssets?.Items ?? []).AssetId);

        var staleReturnResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments/{assigned.Assignment.Id}/return",
            new ReturnAssetRequest
            {
                AssetVersion = assigned.AssetVersion,
                AssignmentVersion = Guid.NewGuid(),
                Condition = AssetConditionContract.Damaged,
                LocationId = returnLocation.Id,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, staleReturnResponse.StatusCode);

        var afterFailedReturn = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(AssetStatusContract.Assigned, afterFailedReturn?.Status);
        Assert.Equal(AssetConditionContract.Good, afterFailedReturn?.Condition);
        Assert.Equal(person.Id, afterFailedReturn?.CurrentAssignment?.Target.Id);

        var deactivateResponse = await client.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = person.ExternalId,
                DisplayName = person.DisplayName,
                Type = person.Type,
                Email = person.Email,
                IsActive = false,
                Version = person.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, deactivateResponse.StatusCode);

        var transferHttpResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                DepartmentId = department.Id,
                AssetVersion = assigned.AssetVersion,
                Notes = "Moved to shared pool",
            },
            ApiTestClient.JsonOptions);
        transferHttpResponse.EnsureSuccessStatusCode();
        var transferred = await transferHttpResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(transferred);
        Assert.Equal(AssignmentTargetTypeContract.Department, transferred.Assignment.Target.Type);
        Assert.Equal(AssetStatusContract.Assigned, transferred.AssetStatus);

        var assignmentHistory =
            await client.GetFromJsonAsync<PagedResponse<AssetAssignmentResponse>>(
                $"/api/v1/assets/{asset.Id}/assignments",
                ApiTestClient.JsonOptions);
        Assert.Equal(2, assignmentHistory?.TotalCount);
        var personAssignment = Assert.Single(
            assignmentHistory?.Items ?? [],
            item => item.Target.Type == AssignmentTargetTypeContract.Person);
        var departmentAssignment = Assert.Single(
            assignmentHistory?.Items ?? [],
            item => item.Target.Type == AssignmentTargetTypeContract.Department);
        Assert.NotNull(personAssignment.ReturnedAtUtc);
        Assert.Null(departmentAssignment.ReturnedAtUtc);

        var currentPersonAssets =
            await client.GetFromJsonAsync<PagedResponse<HolderAssetResponse>>(
                $"/api/v1/people/{person.Id}/assets",
                ApiTestClient.JsonOptions);
        Assert.Empty(currentPersonAssets?.Items ?? []);
        var historicalPersonAssets =
            await client.GetFromJsonAsync<PagedResponse<HolderAssetResponse>>(
                $"/api/v1/people/{person.Id}/assets?includeHistory=true",
                ApiTestClient.JsonOptions);
        Assert.Single(historicalPersonAssets?.Items ?? []);
        var departmentAssets =
            await client.GetFromJsonAsync<PagedResponse<HolderAssetResponse>>(
                $"/api/v1/departments/{department.Id}/assets",
                ApiTestClient.JsonOptions);
        Assert.Equal(asset.Id, Assert.Single(departmentAssets?.Items ?? []).AssetId);

        var returnHttpResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments/{transferred.Assignment.Id}/return",
            new ReturnAssetRequest
            {
                AssetVersion = transferred.AssetVersion,
                AssignmentVersion = transferred.Assignment.Version,
                Condition = AssetConditionContract.Damaged,
                LocationId = returnLocation.Id,
                Notes = "Screen cracked",
            },
            ApiTestClient.JsonOptions);
        returnHttpResponse.EnsureSuccessStatusCode();
        var returned = await returnHttpResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(returned);
        Assert.Equal(AssetStatusContract.InStock, returned.AssetStatus);
        Assert.Equal(AssetConditionContract.Damaged, returned.AssetCondition);
        Assert.Equal(returnLocation.Id, returned.AssetLocation?.Id);
        Assert.NotNull(returned.Assignment.ReturnedAtUtc);

        var returnedAsset = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Null(returnedAsset?.CurrentAssignment);
        Assert.Equal(AssetStatusContract.InStock, returnedAsset?.Status);
        Assert.Equal(returnLocation.Id, returnedAsset?.Location?.Id);

        var history = await client.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        Assert.Contains(history?.Items ?? [], item => item.Type == "Assigned");
        Assert.Contains(history?.Items ?? [], item => item.Type == "Transferred");
        Assert.Contains(history?.Items ?? [], item => item.Type == "Returned");
    }

    [Fact]
    public async Task HolderAndAssignmentRulesReturnSpecificClientErrors()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var person = await CreatePersonAsync(client, $"EXT-{suffix}", "Case Test");

        var missingPerson = await client.GetAsync($"/api/v1/people/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missingPerson.StatusCode);

        var duplicatePerson = await client.PostAsJsonAsync(
            "/api/v1/people",
            new CreatePersonRequest
            {
                ExternalId = person.ExternalId?.ToLowerInvariant(),
                DisplayName = "Duplicate",
                Type = PersonTypeContract.Staff,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicatePerson.StatusCode);

        var invalidPersonType = await client.PostAsJsonAsync(
            "/api/v1/people",
            new
            {
                externalId = $"INVALID-{suffix}",
                displayName = "Invalid Type",
                type = 999,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPersonType.StatusCode);

        var stalePerson = await client.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = person.ExternalId,
                DisplayName = person.DisplayName,
                Type = person.Type,
                Email = person.Email,
                IsActive = true,
                Version = Guid.NewGuid(),
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, stalePerson.StatusCode);

        var department = await CreateDepartmentAsync(
            client,
            $"DEP-{suffix}",
            "Department");
        var duplicateDepartment = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new CreateDepartmentRequest
            {
                Code = department.Code.ToLowerInvariant(),
                Name = "Duplicate",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicateDepartment.StatusCode);

        var category = await CreateCategoryAsync(client, $"RULE-{suffix}");
        var availableAsset = await CreateAssetAsync(
            client,
            category.Id,
            null,
            $"AVL-{suffix}",
            AssetStatusContract.InStock);
        var invalidTarget = await client.PostAsJsonAsync(
            $"/api/v1/assets/{availableAsset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                DepartmentId = department.Id,
                AssetVersion = availableAsset.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTarget.StatusCode);

        var staleAssignment = await client.PostAsJsonAsync(
            $"/api/v1/assets/{availableAsset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = Guid.NewGuid(),
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, staleAssignment.StatusCode);

        var lostAsset = await CreateAssetAsync(
            client,
            category.Id,
            null,
            $"LST-{suffix}",
            AssetStatusContract.Lost);
        var unavailableAssignment = await client.PostAsJsonAsync(
            $"/api/v1/assets/{lostAsset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = lostAsset.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, unavailableAssignment.StatusCode);
    }

    [Fact]
    public async Task RetiringAnAssignedAssetClosesCustodyInTheSameRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, $"RETIRE-{suffix}");
        var asset = await CreateAssetAsync(
            client,
            category.Id,
            null,
            $"RTR-{suffix}",
            AssetStatusContract.InStock);
        var person = await CreatePersonAsync(client, $"RTR-{suffix}", "Retirement Holder");
        var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = asset.Version,
            },
            ApiTestClient.JsonOptions);
        assignmentResponse.EnsureSuccessStatusCode();
        var assigned = await assignmentResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(assigned);

        var retireResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/retire",
            new RetireAssetRequest
            {
                Version = assigned.AssetVersion,
                Reason = "End of useful life",
            },
            ApiTestClient.JsonOptions);
        retireResponse.EnsureSuccessStatusCode();
        var retired = await retireResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.Equal(AssetStatusContract.Retired, retired?.Status);
        Assert.Null(retired?.CurrentAssignment);

        var assignments =
            await client.GetFromJsonAsync<PagedResponse<AssetAssignmentResponse>>(
                $"/api/v1/assets/{asset.Id}/assignments",
                ApiTestClient.JsonOptions);
        Assert.NotNull(Assert.Single(assignments?.Items ?? []).ReturnedAtUtc);

        var history = await client.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        Assert.Contains(history?.Items ?? [], item => item.Type == "Returned");
        Assert.Contains(history?.Items ?? [], item => item.Type == "Retired");
    }

    [Fact]
    public async Task NewCustodyEndpointsEnforceAuthenticationAndPermissions()
    {
        using var anonymous = _factory.CreateHttpsClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/people");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        var readResponse = await auditor.GetAsync("/api/v1/departments");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var writeResponse = await auditor.PostAsJsonAsync(
            "/api/v1/people",
            new CreatePersonRequest
            {
                ExternalId = $"DEN-{Guid.NewGuid():N}",
                DisplayName = "Denied Person",
                Type = PersonTypeContract.Other,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, writeResponse.StatusCode);
    }

    [Fact]
    public async Task OperatorPeopleAccessIsLimitedAndCurrentHolderUsesTheLiveName()
    {
        using var administrator = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(administrator, $"LIMIT-{suffix}");
        var asset = await CreateAssetAsync(
            administrator,
            category.Id,
            null,
            $"LIM-{suffix}",
            AssetStatusContract.InStock);
        var department = await CreateDepartmentAsync(
            administrator,
            $"LIM-{suffix}",
            "Manager-owned department");

        using var operatorClient = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var person = await CreatePersonAsync(
            operatorClient,
            $"LIM-{suffix}",
            "Original Holder Name");
        var assignedResponse = await operatorClient.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                PersonId = person.Id,
                AssetVersion = asset.Version,
            },
            ApiTestClient.JsonOptions);
        assignedResponse.EnsureSuccessStatusCode();
        var assigned = await assignedResponse.Content
            .ReadFromJsonAsync<AssignmentWorkflowResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(assigned);

        var renameResponse = await operatorClient.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = person.ExternalId,
                DisplayName = "Current Holder Name",
                Type = person.Type,
                Email = person.Email,
                IsActive = true,
                Version = person.Version,
            },
            ApiTestClient.JsonOptions);
        renameResponse.EnsureSuccessStatusCode();
        var renamed = await renameResponse.Content.ReadFromJsonAsync<PersonResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(renamed);

        var currentAsset = await operatorClient.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(
            "Current Holder Name",
            currentAsset?.CurrentAssignment?.Target.DisplayName);

        var deactivateResponse = await operatorClient.PutAsJsonAsync(
            $"/api/v1/people/{person.Id}",
            new UpdatePersonRequest
            {
                ExternalId = renamed.ExternalId,
                DisplayName = renamed.DisplayName,
                Type = renamed.Type,
                Email = renamed.Email,
                IsActive = false,
                Version = renamed.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, deactivateResponse.StatusCode);

        var departmentWrite = await operatorClient.PostAsJsonAsync(
            "/api/v1/departments",
            new CreateDepartmentRequest
            {
                Code = $"DEN-{suffix}",
                Name = "Denied department",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, departmentWrite.StatusCode);

        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        var auditorAssignment = await auditor.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                DepartmentId = department.Id,
                AssetVersion = assigned.AssetVersion,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, auditorAssignment.StatusCode);

        var transferResponse = await operatorClient.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/assignments",
            new AssignAssetRequest
            {
                DepartmentId = department.Id,
                AssetVersion = assigned.AssetVersion,
            },
            ApiTestClient.JsonOptions);
        transferResponse.EnsureSuccessStatusCode();

        var assignments =
            await operatorClient.GetFromJsonAsync<PagedResponse<AssetAssignmentResponse>>(
                $"/api/v1/assets/{asset.Id}/assignments",
                ApiTestClient.JsonOptions);
        var closedPersonAssignment = Assert.Single(
            assignments?.Items ?? [],
            item => item.Target.Type == AssignmentTargetTypeContract.Person);
        Assert.Equal("Original Holder Name", closedPersonAssignment.Target.DisplayName);

        var history = await operatorClient.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        var transferEvent = Assert.Single(
            history?.Items ?? [],
            item => item.Type == "Transferred");
        var details = Assert.IsType<JsonElement>(transferEvent.Details);
        Assert.Equal(
            "Original Holder Name",
            details.GetProperty("fromTarget").GetProperty("displayName").GetString());
        Assert.Equal(
            "Manager-owned department",
            details.GetProperty("toTarget").GetProperty("displayName").GetString());
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"CAT-{suffix}",
                Name = "Assignment category",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Category response was empty.");
    }

    private static async Task<LocationResponse> CreateLocationAsync(
        HttpClient client,
        string code,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new CreateLocationRequest { Code = code, Name = name },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Location response was empty.");
    }

    private static async Task<PersonResponse> CreatePersonAsync(
        HttpClient client,
        string externalId,
        string displayName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            new CreatePersonRequest
            {
                ExternalId = externalId,
                DisplayName = displayName,
                Type = PersonTypeContract.Staff,
                Email = $"{externalId.ToLowerInvariant()}@example.test",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PersonResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Person response was empty.");
    }

    private static async Task<DepartmentResponse> CreateDepartmentAsync(
        HttpClient client,
        string code,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/departments",
            new CreateDepartmentRequest { Code = code, Name = name },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DepartmentResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Department response was empty.");
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        Guid categoryId,
        Guid? locationId,
        string assetTag,
        AssetStatusContract status)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = assetTag,
                CategoryId = categoryId,
                LocationId = locationId,
                Status = status,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Asset response was empty.");
    }
}
