using System.Net;
using System.Text.Json;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class OpenApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public OpenApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DevelopmentOpenApiDocumentIsVersion31()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith(
            "3.1.",
            document.RootElement.GetProperty("openapi").GetString(),
            StringComparison.Ordinal);
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/assets", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/people", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/assets/{assetId}/assignments/{assignmentId}/return",
                    out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/assets/{assetId}/qr", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/assets/{assetId}/label", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/labels/batch", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/labels/stocks", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/qr/{token}", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/assets/{assetId}/warranties", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/warranties/expiring", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/users/role-presets", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/imports/assets/validate", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/imports/{importJobId}/commit", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/imports/{importJobId}/errors.csv", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/exports/assets.csv", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/audit/export", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/audit/vocabulary", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/settings/school", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/operations/status", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/legacy-reconciliation/dry-runs",
                    out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/legacy-reconciliation/{importJobId}/sign-off",
                    out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/legacy-reconciliation/{importJobId}/cutover-package",
                    out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/agent/enroll", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/agent/check-ins", out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/assets/{assetId}/device-inventory",
                    out _));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty(
                    "/api/v1/agents/{agentId}/key-recovery",
                    out _));
        var userPath = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/users/{userId}");
        Assert.True(userPath.TryGetProperty("get", out _));
        Assert.True(userPath.TryGetProperty("put", out _));
        Assert.True(userPath.TryGetProperty("delete", out _));

        var cookieScheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("inventoryCookie");
        Assert.Equal("apiKey", cookieScheme.GetProperty("type").GetString());
        Assert.Equal("cookie", cookieScheme.GetProperty("in").GetString());

        var listAssets = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/assets")
            .GetProperty("get");
        Assert.NotEmpty(listAssets.GetProperty("security").EnumerateArray());
        Assert.True(listAssets.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(listAssets.GetProperty("responses").TryGetProperty("403", out _));

        var createAsset = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/assets")
            .GetProperty("post");
        Assert.True(
            createAsset
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .TryGetProperty("example", out _));

        var resolveQr = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/qr/{token}")
            .GetProperty("get");
        Assert.True(
            resolveQr
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .TryGetProperty("example", out _));

        var validateImport = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/imports/assets/validate")
            .GetProperty("post");
        Assert.Contains("imports.validate", validateImport.GetProperty("description").GetString());
        Assert.True(
            validateImport
                .GetProperty("requestBody")
                .GetProperty("content")
                .TryGetProperty("multipart/form-data", out _));
        Assert.True(
            validateImport
                .GetProperty("responses")
                .GetProperty("201")
                .GetProperty("content")
                .GetProperty("application/json")
                .TryGetProperty("example", out _));
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var importJobProperties = schemas
            .GetProperty("AssetImportJobResponse")
            .GetProperty("properties");
        Assert.True(importJobProperties.TryGetProperty("plannedCategoryCreateCount", out _));
        Assert.True(importJobProperties.TryGetProperty("plannedLocationCreateCount", out _));
        Assert.True(importJobProperties.TryGetProperty("inferredDateRows", out _));
        Assert.True(importJobProperties.TryGetProperty("fuzzyPersonMatchRows", out _));
        Assert.True(importJobProperties.TryGetProperty("unassignedImportRows", out _));
        var previewProperties = schemas
            .GetProperty("AssetImportPreviewRowResponse")
            .GetProperty("properties");
        Assert.True(previewProperties.TryGetProperty("resolvedCategoryCode", out _));
        Assert.True(previewProperties.TryGetProperty("categoryWillBeCreated", out _));
        Assert.True(previewProperties.TryGetProperty("resolvedLocationCode", out _));
        Assert.True(previewProperties.TryGetProperty("locationWillBeCreated", out _));
        Assert.True(previewProperties.TryGetProperty("assignmentSource", out _));
        Assert.True(previewProperties.TryGetProperty("assignmentResolution", out _));
        Assert.True(previewProperties.TryGetProperty("inferredDateFields", out _));
        var commitProperties = schemas
            .GetProperty("AssetImportCommitResponse")
            .GetProperty("properties");
        Assert.True(commitProperties.TryGetProperty("createdCategoryCount", out _));
        Assert.True(commitProperties.TryGetProperty("createdLocationCount", out _));
        var stocktakeObservation = schemas
            .GetProperty("RecordStocktakeObservationRequest");
        Assert.Contains(
            stocktakeObservation.GetProperty("required").EnumerateArray(),
            property => property.GetString() == "observedCondition");
        Assert.True(
            stocktakeObservation
                .GetProperty("properties")
                .GetProperty("observedCondition")
                .TryGetProperty("$ref", out _));

        var exportAssets = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/exports/assets.csv")
            .GetProperty("get");
        Assert.Contains("exports.assets", exportAssets.GetProperty("description").GetString());
        Assert.True(
            exportAssets
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .TryGetProperty("text/csv", out _));

        var login = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/login")
            .GetProperty("post");
        Assert.Empty(login.GetProperty("security").EnumerateArray());
        Assert.Contains(
            login.GetProperty("parameters").EnumerateArray(),
            parameter =>
                parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN" &&
                parameter.GetProperty("in").GetString() == "header" &&
                parameter.GetProperty("required").GetBoolean());

        var agentCheckIn = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/agent/check-ins")
            .GetProperty("post");
        Assert.Empty(agentCheckIn.GetProperty("security").EnumerateArray());
        if (agentCheckIn.TryGetProperty(
                "parameters",
                out var agentParameters))
        {
            Assert.DoesNotContain(
                agentParameters.EnumerateArray(),
                parameter =>
                    parameter.GetProperty("name").GetString() ==
                    "X-CSRF-TOKEN");
        }
        Assert.Contains(
            "ECDSA P-256",
            agentCheckIn.GetProperty("description").GetString(),
            StringComparison.Ordinal);
        Assert.True(
            agentCheckIn
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .TryGetProperty("example", out _));

        var json = document.RootElement.GetRawText();
        Assert.DoesNotContain("protectedToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", json, StringComparison.OrdinalIgnoreCase);

        var userResponse = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("UserResponse")
            .GetProperty("properties");
        Assert.True(userResponse.TryGetProperty("archivedAtUtc", out _));
        Assert.True(userResponse.TryGetProperty("version", out _));

        var reconciliationReport = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("LegacyReconciliationReportResponse")
            .GetProperty("properties");
        Assert.True(
            reconciliationReport.TryGetProperty(
                "reportFingerprint",
                out _));
        Assert.True(
            reconciliationReport.TryGetProperty(
                "duplicateIssueCount",
                out _));
        Assert.True(
            reconciliationReport.TryGetProperty(
                "unmappedReferenceIssueCount",
                out _));

        var signOff = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/v1/legacy-reconciliation/{importJobId}/sign-off")
            .GetProperty("post");
        Assert.Contains(
            "never bypass",
            signOff.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryPublishedContractEnumIsAStringWithLegalValues()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var enumTypes = typeof(AssetConditionContract).Assembly
            .GetExportedTypes()
            .Where(type => type.IsEnum)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(enumTypes);
        foreach (var enumType in enumTypes)
        {
            Assert.True(
                schemas.TryGetProperty(enumType.Name, out var schema),
                $"Contract enum schema '{enumType.Name}' was not published.");
            Assert.Equal("string", schema.GetProperty("type").GetString());
            var expected = Enum.GetNames(enumType)
                .Select(JsonNamingPolicy.CamelCase.ConvertName)
                .ToArray();
            var actual = schema
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task OpenApiDocumentsExamplesProblemErrorsAndConditionalPermissionsWithoutDriftGaps()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/assets/{assetId}/dispose", out _));
        var categoryPath = paths.GetProperty("/api/v1/categories/{categoryId}");
        Assert.True(categoryPath.TryGetProperty("get", out _));
        Assert.True(categoryPath.TryGetProperty("put", out _));
        var locationPath = paths.GetProperty("/api/v1/locations/{locationId}");
        Assert.True(locationPath.TryGetProperty("get", out _));
        Assert.True(locationPath.TryGetProperty("put", out _));
        Assert.True(paths.TryGetProperty(
            "/api/v1/imports/reference-data/{importJobId}",
            out _));
        Assert.True(paths.TryGetProperty(
            "/api/v1/imports/reference-data/{importJobId}/commit",
            out _));
        Assert.True(paths.TryGetProperty("/api/v1/exports/people.csv", out _));
        Assert.True(paths.TryGetProperty("/api/v1/exports/departments.csv", out _));
        Assert.True(paths.TryGetProperty("/api/v1/exports/locations.csv", out _));

        var updatePerson = paths
            .GetProperty("/api/v1/people/{personId}")
            .GetProperty("put");
        Assert.Contains(
            "people.manage",
            updatePerson.GetProperty("description").GetString(),
            StringComparison.Ordinal);

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject()
                         .Where(item => IsHttpOperation(item.Name)))
            {
                if (operation.Value.TryGetProperty("requestBody", out var requestBody))
                {
                    foreach (var content in requestBody
                                 .GetProperty("content")
                                 .EnumerateObject()
                                 .Where(item => IsStructuredRequestMediaType(item.Name)))
                    {
                        Assert.True(
                            content.Value.TryGetProperty("example", out _),
                            $"{operation.Name.ToUpperInvariant()} {path.Name} " +
                            $"{content.Name} request is missing an example.");
                    }
                }

                foreach (var documentedResponse in operation.Value
                             .GetProperty("responses")
                             .EnumerateObject())
                {
                    if (int.TryParse(documentedResponse.Name, out var status) &&
                        status >= 400)
                    {
                        Assert.True(
                            documentedResponse.Value.TryGetProperty("content", out var content) &&
                            content.TryGetProperty(
                                "application/problem+json",
                                out var problemContent) &&
                            problemContent.TryGetProperty("schema", out _) &&
                            problemContent.TryGetProperty("example", out _),
                            $"{operation.Name.ToUpperInvariant()} {path.Name} " +
                            $"{status} is missing its Problem Details schema or example.");
                    }

                    if (documentedResponse.Name is not ("200" or "201") ||
                        !documentedResponse.Value.TryGetProperty(
                            "content",
                            out var successContent))
                    {
                        continue;
                    }

                    foreach (var content in successContent.EnumerateObject()
                                 .Where(item => IsJsonMediaType(item.Name)))
                    {
                        if (!content.Value.TryGetProperty("schema", out _))
                        {
                            continue;
                        }

                        Assert.True(
                            content.Value.TryGetProperty("example", out _),
                            $"{operation.Name.ToUpperInvariant()} {path.Name} " +
                            $"{documentedResponse.Name} response is missing an example.");
                    }
                }
            }
        }

        var validateImport = paths
            .GetProperty("/api/v1/imports/assets/validate")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data");
        Assert.True(
            validateImport
                .GetProperty("example")
                .TryGetProperty("mode", out _));

        foreach (var path in new[]
                 {
                     "/api/v1/imports/people/validate",
                     "/api/v1/imports/departments/validate",
                     "/api/v1/imports/locations/validate",
                 })
        {
            var validateReferenceData = paths
                .GetProperty(path)
                .GetProperty("post")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("multipart/form-data");
            Assert.True(
                validateReferenceData
                    .GetProperty("example")
                    .TryGetProperty("mode", out _));
        }
    }

    private static bool IsHttpOperation(string name) =>
        name is "get" or "post" or "put" or "patch" or "delete";

    private static bool IsStructuredRequestMediaType(string name) =>
        IsJsonMediaType(name) ||
        name.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonMediaType(string name) =>
        name.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
}
