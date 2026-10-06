using System.Net;
using System.Net.Http.Json;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class ReferenceDataApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ReferenceDataApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CategoriesSupportPagedSearchArchiveAndRestore()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var create = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"REF-{suffix}",
                Name = $"Reference category {suffix}",
                Description = "Lifecycle test",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var category = await create.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(category);

        var get = await client.GetFromJsonAsync<CategoryResponse>(
            create.Headers.Location,
            ApiTestClient.JsonOptions);
        Assert.Equal(category.Id, get?.Id);

        var page = await client.GetFromJsonAsync<PagedResponse<CategoryResponse>>(
            $"/api/v1/categories?q={suffix}&page=1&pageSize=1",
            ApiTestClient.JsonOptions);
        Assert.Equal(1, page?.PageSize);
        Assert.Contains(page?.Items ?? [], item => item.Id == category.Id);

        var archive = await client.PutAsJsonAsync(
            $"/api/v1/categories/{category.Id}",
            new UpdateCategoryRequest
            {
                Code = category.Code,
                Name = category.Name,
                Description = category.Description,
                IsActive = false,
                Version = category.Version,
            },
            ApiTestClient.JsonOptions);
        archive.EnsureSuccessStatusCode();
        var archived = await archive.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(archived);
        Assert.False(archived.IsActive);

        var activeOnly = await client.GetFromJsonAsync<PagedResponse<CategoryResponse>>(
            $"/api/v1/categories?q={suffix}",
            ApiTestClient.JsonOptions);
        Assert.DoesNotContain(activeOnly?.Items ?? [], item => item.Id == category.Id);

        var restore = await client.PutAsJsonAsync(
            $"/api/v1/categories/{category.Id}",
            new UpdateCategoryRequest
            {
                Code = archived.Code,
                Name = archived.Name,
                Description = archived.Description,
                IsActive = true,
                Version = archived.Version,
            },
            ApiTestClient.JsonOptions);
        restore.EnsureSuccessStatusCode();
        var restored = await restore.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions);
        Assert.True(restored?.IsActive);

        var stale = await client.PutAsJsonAsync(
            $"/api/v1/categories/{category.Id}",
            new UpdateCategoryRequest
            {
                Code = category.Code,
                Name = category.Name,
                Description = category.Description,
                IsActive = true,
                Version = category.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var audit = await dbContext.AuditLogs.AsNoTracking()
            .Where(row => row.TargetType == "category" && row.TargetId == category.Id.ToString())
            .ToArrayAsync();
        Assert.Equal(3, audit.Length);
        Assert.Single(audit, row => row.Action == "category.created");
        Assert.Equal(2, audit.Count(row => row.Action == "category.updated"));
        Assert.All(audit, row => Assert.NotNull(row.ActorUserId));
        Assert.All(audit, row => Assert.Equal("succeeded", row.Outcome));
        Assert.Contains(
            audit,
            row => row.Action == "category.updated" &&
                row.DetailsJson?.Contains("Before", StringComparison.Ordinal) == true &&
                row.DetailsJson.Contains("After", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocationsProtectActiveHierarchyAndRejectCycles()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var parent = await CreateLocationAsync(client, $"P-{suffix}", "Parent", null);
        var child = await CreateLocationAsync(client, $"C-{suffix}", "Child", parent.Id);

        var deactivateParent = await client.PutAsJsonAsync(
            $"/api/v1/locations/{parent.Id}",
            ToUpdate(parent, isActive: false),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, deactivateParent.StatusCode);

        var cycle = await client.PutAsJsonAsync(
            $"/api/v1/locations/{parent.Id}",
            new UpdateLocationRequest
            {
                Code = parent.Code,
                Name = parent.Name,
                Type = parent.Type,
                ParentLocationId = child.Id,
                IsActive = true,
                Version = parent.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);

        var archiveChild = await client.PutAsJsonAsync(
            $"/api/v1/locations/{child.Id}",
            ToUpdate(child, isActive: false),
            ApiTestClient.JsonOptions);
        archiveChild.EnsureSuccessStatusCode();
        var archivedChild = await archiveChild.Content.ReadFromJsonAsync<LocationResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(archivedChild);

        var archiveParent = await client.PutAsJsonAsync(
            $"/api/v1/locations/{parent.Id}",
            ToUpdate(parent, isActive: false),
            ApiTestClient.JsonOptions);
        archiveParent.EnsureSuccessStatusCode();

        var restoreChildWithInactiveParent = await client.PutAsJsonAsync(
            $"/api/v1/locations/{child.Id}",
            ToUpdate(archivedChild, isActive: true),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, restoreChildWithInactiveParent.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var audit = await dbContext.AuditLogs.AsNoTracking()
            .Where(row => row.TargetType == "location" && row.TargetId == child.Id.ToString())
            .ToArrayAsync();
        Assert.Equal(2, audit.Length);
        Assert.Contains(audit, row => row.Action == "location.created");
        Assert.Contains(audit, row => row.Action == "location.updated");
        Assert.All(audit, row => Assert.NotNull(row.ActorUserId));
    }

    private static async Task<LocationResponse> CreateLocationAsync(
        HttpClient client,
        string code,
        string name,
        Guid? parentLocationId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new CreateLocationRequest
            {
                Code = code,
                Name = name,
                Type = "Room",
                ParentLocationId = parentLocationId,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        Assert.NotNull(response.Headers.Location);
        return await response.Content.ReadFromJsonAsync<LocationResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Location response was empty.");
    }

    private static UpdateLocationRequest ToUpdate(
        LocationResponse location,
        bool isActive) =>
        new()
        {
            Code = location.Code,
            Name = location.Name,
            Type = location.Type,
            ParentLocationId = location.ParentLocationId,
            IsActive = isActive,
            Version = location.Version,
        };
}
