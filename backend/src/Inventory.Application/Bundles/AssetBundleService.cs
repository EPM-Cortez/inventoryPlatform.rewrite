using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Bundles;
using Inventory.Domain.Common;

namespace Inventory.Application.Bundles;

public sealed class AssetBundleService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAssetOperationsRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public AssetBundleService(
        IAssetOperationsRepository repository,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
    }

    public Task<PageResult<AssetBundleListItem>> SearchAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalized = new PageRequest(page, pageSize).Normalize();
        return _repository.SearchBundlesAsync(
            includeArchived,
            normalized.Page,
            normalized.PageSize,
            cancellationToken);
    }

    public async Task<AssetBundleView> GetAsync(
        Guid bundleId,
        CancellationToken cancellationToken) =>
        await _repository.GetBundleViewAsync(bundleId, cancellationToken)
        ?? throw new ResourceNotFoundException("Asset bundle", bundleId);

    public async Task<AssetBundleView> CreateAsync(
        CreateAssetBundleCommand command,
        CancellationToken cancellationToken)
    {
        await EnsureIdentifiersAvailableAsync(
            command.Code,
            command.Name,
            excludingBundleId: null,
            cancellationToken);
        var specifications = ToSpecifications(command.Members);
        await ValidateMemberAssetsAsync(
            specifications,
            excludingBundleId: null,
            enforceActiveUniqueness: true,
            cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        AssetBundle bundle;
        try
        {
            bundle = AssetBundle.Create(
                command.Code,
                command.Name,
                command.Description,
                specifications,
                now);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        _repository.Add(bundle);
        AddAudit(
            actor,
            "asset_bundle.created",
            bundle,
            now,
            new
            {
                bundle.Code,
                bundle.Name,
                MemberAssetIds =
                    bundle.CurrentMembers.Select(member => member.AssetId),
            });
        await SaveAsync(cancellationToken);
        return await GetAsync(bundle.Id, cancellationToken);
    }

    public async Task<AssetBundleView> UpdateAsync(
        Guid bundleId,
        UpdateAssetBundleCommand command,
        CancellationToken cancellationToken)
    {
        var bundle = await GetForUpdateAsync(bundleId, cancellationToken);
        await EnsureIdentifiersAvailableAsync(
            command.Code,
            command.Name,
            bundleId,
            cancellationToken);
        var specifications = ToSpecifications(command.Members);
        await ValidateMemberAssetsAsync(
            specifications,
            bundleId,
            enforceActiveUniqueness: !bundle.ArchivedAtUtc.HasValue,
            cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var before = new
        {
            bundle.Code,
            bundle.Name,
            bundle.Description,
            Members = bundle.CurrentMembers
                .Select(
                    member => new
                    {
                        member.AssetId,
                        member.Role,
                        member.OtherRole,
                    })
                .ToArray(),
        };
        try
        {
            bundle.Update(
                command.Code,
                command.Name,
                command.Description,
                specifications,
                command.Version,
                now);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        AddAudit(
            actor,
            "asset_bundle.updated",
            bundle,
            now,
            new
            {
                Before = before,
                After = new
                {
                    bundle.Code,
                    bundle.Name,
                    bundle.Description,
                    Members = bundle.CurrentMembers
                        .Select(
                            member => new
                            {
                                member.AssetId,
                                member.Role,
                                member.OtherRole,
                            }),
                },
            });
        await SaveAsync(cancellationToken);
        return await GetAsync(bundle.Id, cancellationToken);
    }

    public async Task<AssetBundleView> ArchiveAsync(
        Guid bundleId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var bundle = await GetForUpdateAsync(bundleId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        try
        {
            bundle.Archive(version, now);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        AddAudit(
            actor,
            "asset_bundle.archived",
            bundle,
            now,
            new
            {
                bundle.Code,
                MemberAssetIds =
                    bundle.CurrentMembers.Select(member => member.AssetId),
            });
        await SaveAsync(cancellationToken);
        return await GetAsync(bundle.Id, cancellationToken);
    }

    public async Task<AssetBundleView> RestoreAsync(
        Guid bundleId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var bundle = await GetForUpdateAsync(bundleId, cancellationToken);
        await ValidateMemberAssetsAsync(
            bundle.CurrentMembers
                .Select(
                    member => new AssetBundleMemberSpec(
                        member.AssetId,
                        member.Role,
                        member.OtherRole))
                .ToArray(),
            bundle.Id,
            enforceActiveUniqueness: true,
            cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        try
        {
            bundle.Restore(version, now);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        AddAudit(
            actor,
            "asset_bundle.restored",
            bundle,
            now,
            new
            {
                bundle.Code,
                MemberAssetIds =
                    bundle.CurrentMembers.Select(member => member.AssetId),
            });
        await SaveAsync(cancellationToken);
        return await GetAsync(bundle.Id, cancellationToken);
    }

    private async Task<AssetBundle> GetForUpdateAsync(
        Guid bundleId,
        CancellationToken cancellationToken)
    {
        if (bundleId == Guid.Empty)
        {
            throw Validation(
                "bundle_id_required",
                "bundleId",
                "An asset bundle identifier is required.");
        }

        return await _repository.GetBundleForUpdateAsync(
                   bundleId,
                   cancellationToken)
               ?? throw new ResourceNotFoundException("Asset bundle", bundleId);
    }

    private async Task EnsureIdentifiersAvailableAsync(
        string? code,
        string? name,
        Guid? excludingBundleId,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeRequired(code, "code", 50);
        var normalizedName = NormalizeRequired(name, "name", 150);
        if (await _repository.BundleCodeExistsAsync(
                normalizedCode.ToUpperInvariant(),
                excludingBundleId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_bundle_code",
                $"Asset bundle code '{normalizedCode}' is already in use.");
        }

        if (await _repository.BundleNameExistsAsync(
                normalizedName.ToUpperInvariant(),
                excludingBundleId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_bundle_name",
                $"Asset bundle name '{normalizedName}' is already in use.");
        }
    }

    private async Task ValidateMemberAssetsAsync(
        AssetBundleMemberSpec[] members,
        Guid? excludingBundleId,
        bool enforceActiveUniqueness,
        CancellationToken cancellationToken)
    {
        if (members.Length is < 1 or > AssetBundle.MaximumMembers)
        {
            throw Validation(
                "bundle_member_count_invalid",
                "members",
                $"A bundle must contain between 1 and {AssetBundle.MaximumMembers} assets.");
        }

        var assetIds = members.Select(member => member.AssetId).ToArray();
        if (assetIds.Any(assetId => assetId == Guid.Empty) ||
            assetIds.Distinct().Count() != assetIds.Length)
        {
            throw Validation(
                "bundle_member_invalid",
                "members",
                "Bundle members must contain unique, non-empty asset identifiers.");
        }

        var assets = await _repository.GetAssetsForUpdateAsync(
            assetIds,
            cancellationToken);
        if (assets.Count != assetIds.Length)
        {
            throw new ResourceConflictException(
                "bundle_asset_missing",
                "One or more selected bundle assets no longer exist.");
        }

        var terminal = assets.FirstOrDefault(
            asset => asset.Status is AssetStatus.Retired or AssetStatus.Disposed);
        if (terminal is not null)
        {
            throw new ResourceConflictException(
                "bundle_asset_terminal",
                $"Asset '{terminal.AssetTag}' is retired or disposed and cannot join an active bundle.");
        }

        if (!enforceActiveUniqueness)
        {
            return;
        }

        var conflicts = await _repository.GetAssetsInOtherActiveBundlesAsync(
            assetIds,
            excludingBundleId,
            cancellationToken);
        if (conflicts.Count > 0)
        {
            var conflictingAsset = assets.First(asset => conflicts.Contains(asset.Id));
            throw new ResourceConflictException(
                "bundle_asset_already_active",
                $"Asset '{conflictingAsset.AssetTag}' already belongs to another active bundle.");
        }
    }

    private static AssetBundleMemberSpec[] ToSpecifications(
        IReadOnlyCollection<AssetBundleMemberCommand>? members)
    {
        if (members is null)
        {
            throw Validation(
                "bundle_members_required",
                "members",
                "Bundle members are required.");
        }

        return members
            .Select(
                member => new AssetBundleMemberSpec(
                    member.AssetId,
                    member.Role,
                    member.OtherRole))
            .ToArray();
    }

    private void AddAudit(
        CurrentActor actor,
        string action,
        AssetBundle bundle,
        DateTimeOffset now,
        object details)
    {
        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                action,
                "asset_bundle",
                bundle.Id.ToString(),
                now,
                "succeeded",
                JsonSerializer.Serialize(details, JsonOptions),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            var code = exception.Code == "data_conflict"
                ? "bundle_data_conflict"
                : exception.Code;
            throw new ResourceConflictException(code, exception.Message, exception);
        }
    }

    private static string NormalizeRequired(
        string? value,
        string field,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) ||
            normalized.Length > maximumLength)
        {
            throw Validation(
                $"bundle_{field}_invalid",
                field,
                $"{field} is required and must be {maximumLength} characters or fewer.");
        }

        return normalized;
    }

    private static RequestValidationException Validation(
        string code,
        string field,
        string message) =>
        new(
            code,
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static InventoryException MapDomainException(
        DomainRuleException exception) =>
        exception.Code is
            "concurrency_conflict" or
            "bundle_already_archived" or
            "bundle_not_archived"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
