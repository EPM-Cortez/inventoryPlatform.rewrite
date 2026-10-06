using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Inventory.Api.OpenApi;

internal sealed class InventoryStringEnumSchemaTransformer
    : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type =
            Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ??
            context.JsonTypeInfo.Type;
        if (!type.IsEnum)
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum = Enum.GetNames(type)
            .Select(
                name => (JsonNode)JsonValue.Create(
                    JsonNamingPolicy.CamelCase.ConvertName(name))!)
            .ToList();
        return Task.CompletedTask;
    }
}

internal sealed class InventoryOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    internal const string CookieSchemeId = "inventoryCookie";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        document.Info.Title = "Inventory Rewrite API";
        document.Info.Description =
            "Backend API for the single-school inventory rewrite. " +
            "Cookie-authenticated unsafe browser requests also require " +
            "X-CSRF-TOKEN. Signed endpoint-agent ingress uses ECDSA machine " +
            "authentication instead of cookies or browser antiforgery.";
        document.Servers =
        [
            new OpenApiServer
            {
                Url = "/",
                Description = "The host serving this document.",
            },
        ];

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[CookieSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "__Host-Inventory.Auth",
            Description = "Secure ASP.NET Core Identity session cookie.",
        };

        return Task.CompletedTask;
    }
}

internal sealed class InventoryOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    private const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var isAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var authorization = metadata.OfType<IAuthorizeData>().ToArray();
        var isRateLimited = metadata.OfType<EnableRateLimitingAttribute>().Any();
        var ignoresAntiforgery =
            metadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any();
        var requiresAntiforgery =
            RequiresAntiforgery(context.Description.HttpMethod) &&
            !ignoresAntiforgery;
        var policies = authorization
            .Select(item => item.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Select(policy => policy!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        operation.Summary ??= HumanizeOperationId(operation.OperationId);
        operation.Description = BuildDescription(
            operation.Description,
            operation.OperationId,
            isAnonymous,
            authorization.Length > 0,
            policies,
            requiresAntiforgery);

        if (isAnonymous)
        {
            operation.Security = [];
        }
        else
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [
                        new OpenApiSecuritySchemeReference(
                            InventoryOpenApiDocumentTransformer.CookieSchemeId,
                            context.Document,
                            externalResource: null)
                    ] = [],
                },
            ];
        }

        if (requiresAntiforgery)
        {
            operation.Parameters ??= [];
            if (!operation.Parameters.Any(
                    parameter => string.Equals(
                        parameter.Name,
                        AntiforgeryHeaderName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = AntiforgeryHeaderName,
                        In = ParameterLocation.Header,
                        Required = true,
                        Description =
                            "Request token returned by GET /api/v1/auth/csrf. " +
                            "Retain the paired antiforgery cookie.",
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                        },
                    });
            }
        }

        AddStandardErrorResponses(
            operation,
            context.Document
            ?? throw new InvalidOperationException("The OpenAPI document is unavailable."),
            isAnonymous,
            policies.Length > 0,
            requiresAntiforgery,
            isRateLimited);
        AddExamples(operation);
        AddGeneratedExamples(operation);

        return Task.CompletedTask;
    }

    private static void AddStandardErrorResponses(
        OpenApiOperation operation,
        OpenApiDocument document,
        bool isAnonymous,
        bool hasPermissionPolicy,
        bool requiresAntiforgery,
        bool isRateLimited)
    {
        operation.Responses ??= [];
        if (!isAnonymous)
        {
            operation.Responses.TryAdd(
                "401",
                new OpenApiResponse
                {
                    Description = "Authentication is required or the session is invalid.",
                });
        }

        if (hasPermissionPolicy)
        {
            operation.Responses.TryAdd(
                "403",
                new OpenApiResponse
                {
                    Description = "The authenticated user lacks the required permission.",
                });
        }

        if (requiresAntiforgery)
        {
            operation.Responses.TryAdd(
                "400",
                new OpenApiResponse
                {
                    Description =
                        "The request is invalid or its antiforgery token was not accepted.",
                });
        }

        if (isRateLimited)
        {
            operation.Responses.TryAdd(
                "429",
                new OpenApiResponse
                {
                    Description = "The endpoint rate limit was exceeded.",
                });
        }

        foreach (var response in operation.Responses.Where(IsErrorResponse))
        {
            if (response.Value is not OpenApiResponse concreteResponse)
            {
                continue;
            }

            concreteResponse.Content ??=
                new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase);
            if (!concreteResponse.Content.TryGetValue(
                    "application/problem+json",
                    out var mediaType))
            {
                mediaType = new OpenApiMediaType();
                concreteResponse.Content["application/problem+json"] = mediaType;
            }

            mediaType.Schema ??= new OpenApiSchemaReference(
                "ProblemDetails",
                document,
                externalResource: null);
            mediaType.Example ??= CreateProblemExample(response.Key);
        }
    }

    private static void AddExamples(OpenApiOperation operation)
    {
        var requestExample = operation.OperationId switch
        {
            "CreateAssetV1" => ParseExample(
                """
                {
                  "assetTag": "SCH-000123",
                  "categoryId": "11111111-1111-1111-1111-111111111111",
                  "manufacturer": "Dell",
                  "model": "Latitude 5450",
                  "serialNumber": "ABC12345",
                  "status": "inStock",
                  "condition": "good",
                  "currency": "GBP"
                }
                """),
            "UpdateAssetV1" => ParseExample(
                """
                {
                  "assetTag": "SCH-000123",
                  "categoryId": "11111111-1111-1111-1111-111111111111",
                  "status": "repair",
                  "condition": "damaged",
                  "version": "22222222-2222-2222-2222-222222222222"
                }
                """),
            "AssignOrTransferAssetV1" => ParseExample(
                """
                {
                  "personId": "33333333-3333-3333-3333-333333333333",
                  "departmentId": null,
                  "assetVersion": "22222222-2222-2222-2222-222222222222",
                  "notes": "Issued for classroom use."
                }
                """),
            "ReturnAssetAssignmentV1" => ParseExample(
                """
                {
                  "assignmentVersion": "44444444-4444-4444-4444-444444444444",
                  "assetVersion": "22222222-2222-2222-2222-222222222222",
                  "condition": "good",
                  "notes": "Returned to the IT office."
                }
                """),
            "AddAssetWarrantyV1" => ParseExample(
                """
                {
                  "provider": "Example Warranty Ltd",
                  "coverageType": "manufacturer",
                  "startDate": "2026-09-01",
                  "endDate": "2029-08-31",
                  "reference": "WAR-12345",
                  "isPrimary": true
                }
                """),
            "UpdateWarrantyV1" => ParseExample(
                """
                {
                  "provider": "Example Warranty Ltd",
                  "coverageType": "extended",
                  "startDate": "2026-09-01",
                  "endDate": "2030-08-31",
                  "reference": "WAR-12345",
                  "isPrimary": true,
                  "version": "55555555-5555-5555-5555-555555555555"
                }
                """),
            "PrintAssetLabelV1" => ParseExample(
                """
                {
                  "stock": "brotherDk11209",
                  "copies": 1,
                  "startPosition": 1,
                  "issueMissingQr": true
                }
                """),
            "PrintAssetLabelBatchV1" => ParseExample(
                """
                {
                  "stock": "averyL7159",
                  "startPosition": 7,
                  "issueMissingQr": true,
                  "items": [
                    {
                      "assetId": "77777777-7777-7777-7777-777777777777",
                      "quantity": 1
                    },
                    {
                      "assetId": "88888888-8888-8888-8888-888888888888",
                      "quantity": 2
                    }
                  ]
                }
                """),
            "CommitAssetImportV1" => ParseExample(
                """
                {
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            "ValidateAssetImportV1" => ParseExample(
                """
                {
                  "file": "assets.csv",
                  "mode": "createOnly"
                }
                """),
            "ValidatePeopleImportV1" => ParseExample(
                """
                {
                  "file": "staff.csv",
                  "mode": "createOnly"
                }
                """),
            "ValidateDepartmentsImportV1" => ParseExample(
                """
                {
                  "file": "departments.csv",
                  "mode": "upsertByKey"
                }
                """),
            "ValidateLocationsImportV1" => ParseExample(
                """
                {
                  "file": "locations.csv",
                  "mode": "upsertByKey"
                }
                """),
            "CommitReferenceDataImportV1" => ParseExample(
                """
                {
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            "CreateAgentEnrollmentProfileV1" => ParseExample(
                """
                {
                  "name": "ICT Windows rollout",
                  "expiresAtUtc": "2026-07-27T17:00:00Z",
                  "maxEnrollments": 250,
                  "requireTpm": false,
                  "allowedHostnameSuffix": "school.example"
                }
                """),
            "EnrollAgentV1" => ParseExample(
                """
                {
                  "enrollmentToken": "inv_enroll.11111111111111111111111111111111.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "timestampEpochSeconds": 1785081600,
                  "nonce": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                  "deviceId": "win-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAAAAAAAAAAAAAAAAAAAAAAAA\nAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==\n-----END PUBLIC KEY-----",
                  "signatureAlgorithm": "ECDSA_P256_SHA256",
                  "keyProtection": "tpm",
                  "keyThumbprintSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "identity": {
                    "hostname": "ict-pc-001.school.example",
                    "serialNumber": "ABC12345",
                    "biosUuid": "11111111-1111-1111-1111-111111111111",
                    "manufacturer": "Dell",
                    "model": "Latitude 5450"
                  },
                  "signature": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="
                }
                """),
            "CheckInAgentV1" => ParseExample(
                """
                {
                  "agentId": "22222222-2222-2222-2222-222222222222",
                  "deviceId": "win-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "sequence": 1,
                  "timestampEpochSeconds": 1785081600,
                  "nonce": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                  "bodySha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                  "signature": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==",
                  "snapshot": {
                    "snapshotId": "33333333-3333-3333-3333-333333333333",
                    "collectedAtUtc": "2026-07-26T16:00:00Z",
                    "agentVersion": "1.0.0",
                    "hostname": "ict-pc-001.school.example",
                    "domain": "school.example",
                    "hardware": {
                      "manufacturer": "Dell",
                      "model": "Latitude 5450",
                      "serialNumber": "ABC12345",
                      "biosUuid": "11111111-1111-1111-1111-111111111111",
                      "processor": "Example CPU",
                      "processorCores": 8,
                      "logicalProcessors": 16,
                      "memoryBytes": 17179869184
                    },
                    "operatingSystem": {
                      "name": "Windows 11 Enterprise",
                      "version": "10.0",
                      "build": "26100",
                      "architecture": "64-bit"
                    },
                    "security": {
                      "secureBootEnabled": true,
                      "tpmPresent": true,
                      "tpmEnabled": true,
                      "tpmVersion": "2.0",
                      "systemDriveProtected": true,
                      "antivirusProducts": "Microsoft Defender Antivirus"
                    },
                    "networkAdapters": [],
                    "storageVolumes": [],
                    "installedSoftware": []
                  }
                }
                """),
            "LinkManagedAgentV1" => ParseExample(
                """
                {
                  "assetId": "77777777-7777-7777-7777-777777777777",
                  "reason": "Verified against the chassis label",
                  "version": "44444444-4444-4444-4444-444444444444"
                }
                """),
            "RevokeManagedAgentV1" => ParseExample(
                """
                {
                  "reason": "Device decommissioned",
                  "version": "44444444-4444-4444-4444-444444444444"
                }
                """),
            "RevokeAgentEnrollmentProfileV1" => ParseExample(
                """
                {
                  "version": "55555555-5555-5555-5555-555555555555"
                }
                """),
            "UpdateAssetTagPolicyV1" => ParseExample(
                """
                {
                  "prefix": "SCH-",
                  "numericWidth": 6,
                  "nextNumber": 124,
                  "version": "11111111-1111-1111-1111-111111111111"
                }
                """),
            "PreviewAssetBulkChangeV1" => ParseExample(
                """
                {
                  "assets": [
                    {
                      "assetId": "77777777-7777-7777-7777-777777777777",
                      "version": "22222222-2222-2222-2222-222222222222"
                    },
                    {
                      "assetId": "88888888-8888-8888-8888-888888888888",
                      "version": "33333333-3333-3333-3333-333333333333"
                    }
                  ],
                  "operation": {
                    "kind": "setLocation",
                    "locationId": "66666666-6666-6666-6666-666666666666",
                    "clearLocation": false
                  },
                  "reason": "Moved into the refurbished ICT store"
                }
                """),
            "CommitAssetBulkChangeV1" => ParseExample(
                """
                {
                  "fingerprint": "B8257A295AA556021BFB24A4584D94D1143CF1566509F48B2E7AE65C46360BB0",
                  "version": "44444444-4444-4444-4444-444444444444"
                }
                """),
            "StartStocktakeV1" => ParseExample(
                """
                {
                  "name": "Summer keyboard stocktake",
                  "scope": "location",
                  "locationId": "66666666-6666-6666-6666-666666666666",
                  "categoryId": null
                }
                """),
            "RecordStocktakeObservationV1" => ParseExample(
                """
                {
                  "assetId": "77777777-7777-7777-7777-777777777777",
                  "assetTag": null,
                  "observedLocationId": "66666666-6666-6666-6666-666666666666",
                  "observedCondition": "good",
                  "note": "Checked in the library store",
                  "version": "55555555-5555-5555-5555-555555555555"
                }
                """),
            "CompleteStocktakeV1" => ParseExample(
                """
                {
                  "version": "55555555-5555-5555-5555-555555555555"
                }
                """),
            "CancelStocktakeV1" => ParseExample(
                """
                {
                  "version": "55555555-5555-5555-5555-555555555555",
                  "reason": "The room became unavailable"
                }
                """),
            "UpdateAssetAssignmentExpectedReturnV1" => ParseExample(
                """
                {
                  "expectedReturnOn": "2026-09-30",
                  "assignmentVersion": "44444444-4444-4444-4444-444444444444"
                }
                """),
            "CreateLeaverCaseV1" => ParseExample(
                """
                {
                  "personId": "33333333-3333-3333-3333-333333333333",
                  "leavingOn": "2026-08-31",
                  "notes": "Recover equipment before the final working day"
                }
                """),
            "UpdateLeaverCaseV1" => ParseExample(
                """
                {
                  "leavingOn": "2026-09-04",
                  "notes": "Leaving date updated by HR",
                  "version": "66666666-6666-6666-6666-666666666666"
                }
                """),
            "CompleteLeaverCaseV1" or
            "CancelLeaverCaseV1" or
            "MarkLeaverRehiredV1" => ParseExample(
                """
                {
                  "version": "66666666-6666-6666-6666-666666666666"
                }
                """),
            "CreateAssetBundleV1" => ParseExample(
                """
                {
                  "code": "KIT-0042",
                  "name": "Teacher laptop kit 42",
                  "members": [
                    {
                      "assetId": "77777777-7777-7777-7777-777777777777",
                      "role": "laptop",
                      "otherRole": null
                    },
                    {
                      "assetId": "88888888-8888-8888-8888-888888888888",
                      "role": "charger",
                      "otherRole": null
                    }
                  ]
                }
                """),
            "CreateSavedAssetSearchV1" => ParseExample(
                """
                {
                  "name": "Unassigned laptops",
                  "schemaVersion": 1,
                  "filter": {
                    "query": "laptop",
                    "assigned": false,
                    "pageSize": 50,
                    "sort": "-updatedAtUtc"
                  }
                }
                """),
            "SetDefaultSavedAssetSearchV1" or
            "ArchiveSavedAssetSearchV1" or
            "ArchiveAssetBundleV1" or
            "RestoreAssetBundleV1" or
            "ArchiveAssetAttachmentV1" => ParseExample(
                """
                {
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            "UploadAssetAttachmentV1" => ParseExample(
                """
                {
                  "category": "invoice",
                  "file": "invoice-2026-0042.pdf"
                }
                """),
            "CreateAgentKeyRecoveryV1" => ParseExample(
                """
                {
                  "reason": "TPM was cleared during motherboard replacement",
                  "expiresAtUtc": "2026-07-27T17:30:00Z",
                  "requireTpm": false,
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            "RevokeAgentKeyRecoveryV1" => ParseExample(
                """
                {
                  "reason": "The repair was cancelled.",
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            _ => null,
        };

        if (requestExample is not null && operation.RequestBody?.Content is not null)
        {
            foreach (var mediaType in operation.RequestBody.Content.Values)
            {
                mediaType.Example = requestExample.DeepClone();
            }
        }

        var responseExample = operation.OperationId switch
        {
            "IssueAssetQrV1" or "RotateAssetQrV1" => ParseExample(
                """
                {
                  "id": "66666666-6666-6666-6666-666666666666",
                  "assetId": "77777777-7777-7777-7777-777777777777",
                  "tokenVersion": 2,
                  "issuedAtUtc": "2026-07-24T12:00:00Z",
                  "payloadUrl": "https://inventory.school.example/scan/example-opaque-token",
                  "imageUrl": "/api/v1/assets/77777777-7777-7777-7777-777777777777/qr"
                }
                """),
            "ResolveAssetQrV1" => ParseExample(
                """
                {
                  "id": "77777777-7777-7777-7777-777777777777",
                  "assetTag": "SCH-000123",
                  "category": {
                    "id": "11111111-1111-1111-1111-111111111111",
                    "code": "LAP",
                    "name": "Laptop"
                  },
                  "status": "inStock",
                  "condition": "good",
                  "location": null,
                  "warrantySummary": {
                    "state": "active",
                    "provider": "Example Warranty Ltd",
                    "endsOn": "2029-08-31",
                    "daysRemaining": 1134
                  }
                }
                """),
            "AddAssetWarrantyV1" or "UpdateWarrantyV1" => ParseExample(
                """
                {
                  "id": "88888888-8888-8888-8888-888888888888",
                  "assetId": "77777777-7777-7777-7777-777777777777",
                  "provider": "Example Warranty Ltd",
                  "coverageType": "manufacturer",
                  "startDate": "2026-09-01",
                  "endDate": "2029-08-31",
                  "reference": "WAR-12345",
                  "notes": null,
                  "isPrimary": true,
                  "state": "future",
                  "daysRemaining": 1134,
                  "createdAtUtc": "2026-07-24T12:00:00Z",
                  "updatedAtUtc": "2026-07-24T12:00:00Z",
                  "archivedAtUtc": null,
                  "version": "55555555-5555-5555-5555-555555555555"
                }
                """),
            "ListAgentKeyRecoveryAuthorizationsV1" => ParseExample(
                """
                {
                  "items": [
                    {
                      "authorizationId": "11111111-1111-1111-1111-111111111111",
                      "agentId": "22222222-2222-2222-2222-222222222222",
                      "status": "pending",
                      "reason": "TPM was cleared during motherboard replacement",
                      "createdAtUtc": "2026-07-27T17:00:00Z",
                      "createdByUserId": "33333333-3333-3333-3333-333333333333",
                      "createdByDisplayName": "Inventory Administrator",
                      "expiresAtUtc": "2026-07-27T17:30:00Z",
                      "requireTpm": false,
                      "consumedAtUtc": null,
                      "revokedAtUtc": null,
                      "revokedByUserId": null,
                      "revokedReason": null,
                      "version": "44444444-4444-4444-4444-444444444444"
                    }
                  ],
                  "page": 1,
                  "pageSize": 50,
                  "totalCount": 1
                }
                """),
            "GetAuditFilterVocabularyV1" => ParseExample(
                """
                {
                  "actions": [
                    "assets.import.committed",
                    "authentication.login",
                    "saved_search.created"
                  ],
                  "targetTypes": [
                    "asset_import_job",
                    "user"
                  ],
                  "outcomes": [
                    "failed",
                    "succeeded",
                    "validation_failed"
                  ],
                  "truncated": false
                }
                """),
            "ValidateAssetImportV1" => ParseExample(
                """
                {
                  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "schemaVersion": 2,
                  "originalFileName": "assets.csv",
                  "fileHash": "7F83B1657FF1FC53B92DC18148A1D65DFC2D4B1FA3D677284ADDD200126D9069",
                  "mode": "createOnly",
                  "status": "readyToCommit",
                  "totalRows": 1,
                  "validRows": 1,
                  "errorRows": 0,
                  "createRows": 1,
                  "updateRows": 0,
                  "generatedAssetTagRows": 1,
                  "plannedCategoryCreateCount": 1,
                  "plannedLocationCreateCount": 1,
                  "inferredDateRows": 1,
                  "fuzzyPersonMatchRows": 0,
                  "unassignedImportRows": 1,
                  "preview": [
                    {
                      "rowNumber": 2,
                      "action": "create",
                      "assetTag": "STR-AUTO-7F83B1657FF1FC53-0002",
                      "assetTagGenerated": true,
                      "category": "Laptop",
                      "resolvedCategoryCode": "LAPTOP",
                      "categoryWillBeCreated": true,
                      "status": "inStock",
                      "condition": "good",
                      "locationCode": "ICT Support",
                      "resolvedLocationCode": "ICT-SUPPORT",
                      "locationWillBeCreated": true,
                      "assignmentSource": "FormerStaff",
                      "assignmentResolution": "unassigned",
                      "assignedTo": null,
                      "purchaseDate": "2021-01-01",
                      "warrantyStartDate": null,
                      "warrantyEndDate": null,
                      "inferredDateFields": [
                        "purchaseDate"
                      ]
                    }
                  ],
                  "errors": [],
                  "previewTruncated": false,
                  "errorsTruncated": false,
                  "version": "99999999-9999-9999-9999-999999999999"
                }
                """),
            "CommitAssetImportV1" => ParseExample(
                """
                {
                  "importJobId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "status": "committed",
                  "createdRows": 1,
                  "updatedRows": 0,
                  "unchangedRows": 0,
                  "createdCategoryCount": 1,
                  "createdLocationCount": 1,
                  "committedAtUtc": "2026-07-25T12:00:00Z",
                  "version": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                }
                """),
            "ValidatePeopleImportV1" or "GetReferenceDataImportJobV1" =>
                CreateReferenceDataImportJobExample(
                    "people",
                    "staff.csv",
                    "j.smith",
                    "Jane Smith",
                    "staff",
                    parentKey: null),
            "ValidateDepartmentsImportV1" =>
                CreateReferenceDataImportJobExample(
                    "departments",
                    "departments.csv",
                    "SIXTH-FORM",
                    "Sixth Form",
                    type: null,
                    parentKey: null),
            "ValidateLocationsImportV1" =>
                CreateReferenceDataImportJobExample(
                    "locations",
                    "locations.csv",
                    "B16",
                    "B16",
                    "Room",
                    parentKey: null),
            "CommitReferenceDataImportV1" => ParseExample(
                """
                {
                  "importJobId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                  "resource": "people",
                  "status": "committed",
                  "createdRows": 1,
                  "updatedRows": 0,
                  "unchangedRows": 0,
                  "committedAtUtc": "2026-07-25T12:00:00Z",
                  "version": "dddddddd-dddd-dddd-dddd-dddddddddddd"
                }
                """),
            _ => null,
        };

        if (responseExample is null || operation.Responses is null)
        {
            return;
        }

        var successResponse = operation.Responses.TryGetValue("201", out var created)
            ? created
            : operation.Responses.TryGetValue("200", out var ok)
                ? ok
                : null;
        if (successResponse?.Content is null)
        {
            return;
        }

        foreach (var mediaType in successResponse.Content.Values)
        {
            if (mediaType.Schema is not null)
            {
                mediaType.Example = responseExample.DeepClone();
            }
        }
    }

    private static JsonObject CreateReferenceDataImportJobExample(
        string resource,
        string fileName,
        string key,
        string name,
        string? type,
        string? parentKey) =>
        new JsonObject
        {
            ["id"] = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            ["resource"] = resource,
            ["schemaVersion"] = 1,
            ["originalFileName"] = fileName,
            ["fileHash"] =
                "7F83B1657FF1FC53B92DC18148A1D65DFC2D4B1FA3D677284ADDD200126D9069",
            ["mode"] = "createOnly",
            ["status"] = "readyToCommit",
            ["totalRows"] = 1,
            ["validRows"] = 1,
            ["errorRows"] = 0,
            ["createRows"] = 1,
            ["updateRows"] = 0,
            ["generatedKeyRows"] = resource == "departments" ? 1 : 0,
            ["createdAtUtc"] = "2026-07-25T12:00:00Z",
            ["createdByUserId"] = "11111111-1111-1111-1111-111111111111",
            ["createdByDisplayName"] = "Inventory Administrator",
            ["committedAtUtc"] = null,
            ["committedByUserId"] = null,
            ["committedByDisplayName"] = null,
            ["committedCreateRows"] = 0,
            ["committedUpdateRows"] = 0,
            ["committedUnchangedRows"] = 0,
            ["preview"] = new JsonArray
            {
                new JsonObject
                {
                    ["rowNumber"] = 2,
                    ["action"] = "create",
                    ["key"] = key,
                    ["keyGenerated"] = resource == "departments",
                    ["name"] = name,
                    ["type"] = type,
                    ["parentKey"] = parentKey,
                    ["isActive"] = true,
                },
            },
            ["errors"] = new JsonArray(),
            ["previewTruncated"] = false,
            ["errorsTruncated"] = false,
            ["version"] = "99999999-9999-9999-9999-999999999999",
        };

    private static void AddGeneratedExamples(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content is not null)
        {
            foreach (var content in operation.RequestBody.Content)
            {
                if (content.Value.Example is null &&
                    content.Value.Schema is not null &&
                    IsStructuredRequestMediaType(content.Key))
                {
                    content.Value.Example = CreateSchemaExample(
                        content.Value.Schema,
                        new HashSet<string>(StringComparer.Ordinal),
                        propertyName: null);
                }
            }
        }

        if (operation.Responses is null)
        {
            return;
        }

        foreach (var response in operation.Responses
                     .Where(item => item.Key is "200" or "201"))
        {
            if (response.Value.Content is null)
            {
                continue;
            }

            foreach (var content in response.Value.Content)
            {
                if (content.Value.Example is null &&
                    content.Value.Schema is not null &&
                    IsJsonMediaType(content.Key))
                {
                    content.Value.Example = CreateSchemaExample(
                        content.Value.Schema,
                        new HashSet<string>(StringComparer.Ordinal),
                        propertyName: null);
                }
            }
        }
    }

    private static JsonNode? CreateSchemaExample(
        IOpenApiSchema schema,
        HashSet<string> visitedReferences,
        string? propertyName)
    {
        if (schema.Example is not null)
        {
            return schema.Example.DeepClone();
        }

        if (schema.Const is not null)
        {
            return JsonValue.Create(schema.Const);
        }

        if (schema.Enum is { Count: > 0 })
        {
            return schema.Enum[0].DeepClone();
        }

        if (schema is OpenApiSchemaReference reference)
        {
            if (!string.IsNullOrWhiteSpace(reference.Id) &&
                !visitedReferences.Add(reference.Id))
            {
                return null;
            }

            if (reference.Target is not null)
            {
                return CreateSchemaExample(reference.Target, visitedReferences, propertyName);
            }
        }

        if (schema.Properties is { Count: > 0 })
        {
            var result = new JsonObject();
            foreach (var property in schema.Properties)
            {
                result[property.Key] = CreateSchemaExample(
                    property.Value,
                    new HashSet<string>(visitedReferences, StringComparer.Ordinal),
                    property.Key);
            }

            return result;
        }

        if (schema.Items is not null)
        {
            return new JsonArray(
                CreateSchemaExample(
                    schema.Items,
                    new HashSet<string>(visitedReferences, StringComparer.Ordinal),
                    propertyName));
        }

        if (schema.Default is not null)
        {
            return schema.Default.DeepClone();
        }

        if (schema.Type.HasValue)
        {
            if (schema.Type.Value.HasFlag(JsonSchemaType.Boolean))
            {
                return JsonValue.Create(true);
            }

            if (schema.Type.Value.HasFlag(JsonSchemaType.Integer))
            {
                return JsonValue.Create(1);
            }

            if (schema.Type.Value.HasFlag(JsonSchemaType.Number))
            {
                return JsonValue.Create(1.0m);
            }
        }

        return CreateStringExample(schema.Format, propertyName);
    }

    private static JsonValue CreateStringExample(string? format, string? propertyName)
    {
        if (string.Equals(format, "uuid", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("11111111-1111-1111-1111-111111111111")!;
        }

        if (string.Equals(format, "date", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("2026-07-25")!;
        }

        if (string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("2026-07-25T12:00:00Z")!;
        }

        if (string.Equals(format, "email", StringComparison.OrdinalIgnoreCase) ||
            propertyName?.Contains("email", StringComparison.OrdinalIgnoreCase) == true)
        {
            return JsonValue.Create("user@inventory.example")!;
        }

        if (string.Equals(format, "uri", StringComparison.OrdinalIgnoreCase) ||
            propertyName?.EndsWith("Url", StringComparison.OrdinalIgnoreCase) == true)
        {
            return JsonValue.Create("https://inventory.school.example/example")!;
        }

        if (string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propertyName, "file", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("assets.csv")!;
        }

        if (propertyName?.EndsWith("Id", StringComparison.OrdinalIgnoreCase) == true ||
            string.Equals(propertyName, "version", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create("11111111-1111-1111-1111-111111111111")!;
        }

        if (propertyName?.Contains("password", StringComparison.OrdinalIgnoreCase) == true)
        {
            return JsonValue.Create("Example-Password!42")!;
        }

        return JsonValue.Create(
            propertyName switch
            {
                "assetTag" => "SCH-000123",
                "code" => "ICT",
                "name" => "Example",
                "displayName" => "Example User",
                _ => "example",
            })!;
    }

    private static JsonObject CreateProblemExample(string statusCode)
    {
        var status = int.TryParse(statusCode, out var parsed) ? parsed : 400;
        return new JsonObject
        {
            ["type"] = $"https://inventory.local/problems/http-{status}",
            ["title"] = status switch
            {
                400 => "The request could not be completed.",
                401 => "Authentication is required.",
                403 => "Permission denied.",
                404 => "The requested resource was not found.",
                409 => "The requested change conflicts with existing data.",
                429 => "Too many requests.",
                _ => "The request failed.",
            },
            ["status"] = status,
            ["detail"] = "Review the request and retry.",
            ["instance"] = "/api/v1/example",
            ["traceId"] = "00-example-trace-id",
        };
    }

    private static bool IsErrorResponse(KeyValuePair<string, IOpenApiResponse> response) =>
        int.TryParse(response.Key, out var status) && status >= 400;

    private static bool IsStructuredRequestMediaType(string mediaType) =>
        IsJsonMediaType(mediaType) ||
        mediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonMediaType(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    private static JsonNode ParseExample(string json) =>
        JsonNode.Parse(json)
        ?? throw new InvalidOperationException("OpenAPI example JSON was empty.");

    private static bool RequiresAntiforgery(string? method) =>
        method is not null &&
        (HttpMethods.IsPost(method) ||
         HttpMethods.IsPut(method) ||
         HttpMethods.IsPatch(method) ||
         HttpMethods.IsDelete(method));

    private static string BuildDescription(
        string? existingDescription,
        string? operationId,
        bool isAnonymous,
        bool hasAuthorizationMetadata,
        string[] policies,
        bool requiresAntiforgery)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(existingDescription))
        {
            parts.Add(existingDescription.Trim());
        }

        if (operationId == "UpdatePersonV1")
        {
            parts.Add(
                "Setting `isActive` to `false` additionally requires the `people.manage` permission.");
        }

        if (operationId == "ValidateAssetImportV1")
        {
            parts.Add(
                "Validation is read-only. Unmatched category/location values are returned " +
                "as planned creates; year-only, month/year, and supported legacy serial " +
                "dates are returned as normalized preview values.");
        }

        if (operationId == "CommitAssetImportV1")
        {
            parts.Add(
                "Creates each distinct planned category/location and applies all asset, " +
                "custody, and warranty changes in one SQLite transaction.");
        }

        if (operationId == "CreateLegacyReconciliationDryRunV1")
        {
            parts.Add(
                "Persists an ordinary import validation job plus deterministic " +
                "source/target counts, duplicate/unmapped issues, sampled comparisons, " +
                "and a SHA-256 report fingerprint in one transaction. It does not change live assets.");
        }

        if (operationId == "SignOffLegacyReconciliationV1")
        {
            parts.Add(
                "Creates one immutable authenticated decision. Signed exceptions are " +
                "evidence only and never bypass validation, snapshot, version, or commit checks.");
        }

        if (operationId == "GetLegacyCutoverPackageV1")
        {
            parts.Add(
                "Returns a no-store JSON evidence package with readiness blockers and " +
                "explicit cutover/rollback procedures.");
        }

        if (operationId == "ExportAuditLogsV1")
        {
            parts.Add(
                "The bounded formula-safe CSV excludes structured audit details and " +
                "reports row count/truncation in response headers.");
        }

        if (operationId is
            "BeginOwnMfaEnrollmentV1" or
            "ConfirmOwnMfaEnrollmentV1" or
            "RegenerateOwnMfaRecoveryCodesV1")
        {
            parts.Add(
                "Sensitive MFA material is returned with no-store handling and is never audited.");
        }

        if (operationId == "EnrollAgentV1")
        {
            parts.Add(
                "Machine authentication: a short-lived, usage-capped enrollment " +
                "profile token plus an ECDSA P-256 signature over the canonical " +
                "identity request is required. Browser antiforgery does not apply.");
        }

        if (operationId == "CheckInAgentV1")
        {
            parts.Add(
                "Machine authentication: the enrolled ECDSA P-256 key signs the " +
                "agent id, device id, monotonically increasing sequence, snapshot " +
                "id, current timestamp, nonce, and snapshot SHA-256. Browser " +
                "antiforgery does not apply.");
        }

        if (operationId == "CreateAgentEnrollmentProfileV1")
        {
            parts.Add(
                "Returns the enrollment token once with no-store handling. Only its " +
                "SHA-256 hash is persisted; revoke the profile immediately after a rollout.");
        }

        if (operationId == "GetAssetDeviceInventoryV1")
        {
            parts.Add(
                "Returns agent-reported telemetry as separate provenance. It does " +
                "not silently overwrite manually curated asset fields.");
        }

        if (operationId == "CreateAgentKeyRecoveryV1")
        {
            parts.Add(
                "Returns the recovery token once with no-store handling. " +
                "Only its SHA-256 hash is persisted.");
        }

        if (operationId == "ListAgentKeyRecoveryAuthorizationsV1")
        {
            parts.Add(
                "Returns token-free authorization metadata. By default only " +
                "pending, unexpired authorizations are returned; includeClosed " +
                "adds expired, consumed, and revoked records.");
        }

        if (isAnonymous)
        {
            parts.Add("Authentication: anonymous.");
        }
        else if (policies.Length > 0)
        {
            parts.Add($"Required permission: `{string.Join("`, `", policies)}`.");
        }
        else if (hasAuthorizationMetadata)
        {
            parts.Add("Authentication: an enabled user session is required.");
        }

        if (requiresAntiforgery)
        {
            parts.Add($"Antiforgery: the `{AntiforgeryHeaderName}` header is required.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string HumanizeOperationId(string? operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return "Inventory API operation";
        }

        var source = operationId.EndsWith("V1", StringComparison.Ordinal)
            ? operationId[..^2]
            : operationId;
        var builder = new StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(source[index - 1]) ||
                 (index + 1 < source.Length && char.IsLower(source[index + 1]))))
            {
                builder.Append(' ');
            }

            builder.Append(index == 0 ? current : char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
