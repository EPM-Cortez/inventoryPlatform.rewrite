using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.AssetTags;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;

namespace Inventory.Application.AssetTags;

public sealed class AssetTagPolicyService
{
    private const int MaximumCollisionSkips = 100;
    private readonly IAssetOperationsRepository _operationsRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public AssetTagPolicyService(
        IAssetOperationsRepository operationsRepository,
        IInventoryRepository inventoryRepository,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _operationsRepository = operationsRepository;
        _inventoryRepository = inventoryRepository;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<AssetTagPolicyView> GetAsync(
        CancellationToken cancellationToken)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        return ToView(policy);
    }

    public async Task<AssetTagPolicyView> UpdateAsync(
        UpdateAssetTagPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var before = ToView(policy);
        try
        {
            policy.Update(
                command.Prefix,
                command.NumericWidth,
                command.NextNumber,
                command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        _operationsRepository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "asset_tag_policy.updated",
                "asset_tag_policy",
                AssetTagPolicy.SingletonId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        Before = new
                        {
                            before.Prefix,
                            before.NumericWidth,
                            before.NextNumber,
                        },
                        After = new
                        {
                            policy.Prefix,
                            policy.NumericWidth,
                            policy.NextNumber,
                        },
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveAsync(cancellationToken);
        return ToView(policy);
    }

    public async Task<string> ResolveForCreateAsync(
        string? suppliedAssetTag,
        bool generateAssetTag,
        CancellationToken cancellationToken)
    {
        var normalizedSupplied = string.IsNullOrWhiteSpace(suppliedAssetTag)
            ? null
            : suppliedAssetTag.Trim();
        if (generateAssetTag == (normalizedSupplied is not null))
        {
            throw new RequestValidationException(
                "asset_tag_source_invalid",
                "Supply assetTag or request generateAssetTag, but not both.",
                new Dictionary<string, string[]>
                {
                    ["assetTag"] =
                    [
                        "Supply assetTag or request generateAssetTag, but not both.",
                    ],
                    ["generateAssetTag"] =
                    [
                        "Supply assetTag or request generateAssetTag, but not both.",
                    ],
                });
        }

        if (normalizedSupplied is not null)
        {
            return normalizedSupplied;
        }

        for (var attempt = 0; attempt < MaximumCollisionSkips; attempt++)
        {
            var generated = await _operationsRepository.ReserveNextAssetTagAsync(
                cancellationToken);
            if (generated is null)
            {
                throw new ResourceConflictException(
                    "asset_tag_sequence_exhausted",
                    "The configured asset-tag sequence has been exhausted.");
            }

            if (!await _inventoryRepository.AssetTagExistsAsync(
                    generated.ToUpperInvariant(),
                    excludingAssetId: null,
                    cancellationToken))
            {
                return generated;
            }
        }

        throw new ResourceConflictException(
            "asset_tag_allocation_conflict",
            "The next generated asset tags already exist. Review the asset-tag policy.");
    }

    private async Task<AssetTagPolicy> GetPolicyAsync(
        CancellationToken cancellationToken) =>
        await _operationsRepository.GetAssetTagPolicyAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The singleton asset-tag policy is not configured.");

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _operationsRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private static AssetTagPolicyView ToView(AssetTagPolicy policy) =>
        new(
            policy.Prefix,
            policy.NumericWidth,
            policy.NextNumber,
            policy.PreviewNextTag(),
            policy.Version);

    private static InventoryException MapDomainException(
        DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
