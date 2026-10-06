using Inventory.Domain.Common;

namespace Inventory.Domain.Bundles;

public enum AssetBundleMemberRole
{
    Laptop,
    Charger,
    Dock,
    Case,
    Other,
}

public sealed record AssetBundleMemberSpec(
    Guid AssetId,
    AssetBundleMemberRole Role,
    string? OtherRole);

public sealed class AssetBundle
{
    public const int MaximumMembers = 50;

    private readonly List<AssetBundleMember> _members = [];

    private AssetBundle()
    {
    }

    private AssetBundle(
        Guid id,
        string code,
        string name,
        string? description,
        IReadOnlyCollection<AssetBundleMemberSpec> members,
        DateTimeOffset createdAtUtc)
    {
        EnsureUtc(createdAtUtc);
        Id = id;
        SetDetails(code, name, description);
        ReplaceMembers(members, createdAtUtc);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string CodeNormalized { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid Version { get; private set; }

    public IReadOnlyCollection<AssetBundleMember> Members => _members.AsReadOnly();

    public IReadOnlyCollection<AssetBundleMember> CurrentMembers =>
        _members.Where(member => member.RemovedAtUtc is null).ToArray();

    public static AssetBundle Create(
        string code,
        string name,
        string? description,
        IReadOnlyCollection<AssetBundleMemberSpec> members,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), code, name, description, members, createdAtUtc);

    public void Update(
        string code,
        string name,
        string? description,
        IReadOnlyCollection<AssetBundleMemberSpec> members,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc);
        EnsureVersion(expectedVersion);
        if (ArchivedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "bundle_already_archived",
                "Restore the asset bundle before changing its details or members.");
        }

        ValidateMemberSpecs(members);
        SetDetails(code, name, description);
        ReplaceMembers(members, updatedAtUtc);
        UpdatedAtUtc = updatedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Archive(Guid expectedVersion, DateTimeOffset archivedAtUtc)
    {
        EnsureUtc(archivedAtUtc);
        EnsureVersion(expectedVersion);
        if (ArchivedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "bundle_already_archived",
                "The asset bundle is already archived.");
        }

        ArchivedAtUtc = archivedAtUtc;
        foreach (var member in CurrentMembers)
        {
            member.SetBundleActive(false);
        }

        UpdatedAtUtc = archivedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Restore(Guid expectedVersion, DateTimeOffset restoredAtUtc)
    {
        EnsureUtc(restoredAtUtc);
        EnsureVersion(expectedVersion);
        if (!ArchivedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "bundle_not_archived",
                "Only an archived asset bundle can be restored.");
        }

        ArchivedAtUtc = null;
        foreach (var member in CurrentMembers)
        {
            member.SetBundleActive(true);
        }

        UpdatedAtUtc = restoredAtUtc;
        Version = Guid.NewGuid();
    }

    private void SetDetails(string code, string name, string? description)
    {
        var normalizedCode = TextRules.Required(code, nameof(Code), 50);
        var normalizedName = TextRules.Required(name, nameof(Name), 150);
        var normalizedDescription = TextRules.Optional(
            description,
            nameof(Description),
            1000);
        Code = normalizedCode;
        CodeNormalized = TextRules.NormalizeKey(normalizedCode);
        Name = normalizedName;
        NameNormalized = TextRules.NormalizeKey(normalizedName);
        Description = normalizedDescription;
    }

    private void ReplaceMembers(
        IReadOnlyCollection<AssetBundleMemberSpec> members,
        DateTimeOffset changedAtUtc)
    {
        ValidateMemberSpecs(members);

        var incomingIds = members.Select(member => member.AssetId).ToHashSet();
        foreach (var existing in CurrentMembers.Where(
                     member => !incomingIds.Contains(member.AssetId)))
        {
            existing.Remove(changedAtUtc);
        }

        foreach (var specification in members)
        {
            var existing = _members.SingleOrDefault(
                member => member.AssetId == specification.AssetId);
            if (existing is null)
            {
                _members.Add(
                    AssetBundleMember.Create(
                        Id,
                        specification,
                        bundleIsActive: !ArchivedAtUtc.HasValue,
                        changedAtUtc));
            }
            else
            {
                existing.Include(
                    specification.Role,
                    specification.OtherRole,
                    bundleIsActive: !ArchivedAtUtc.HasValue,
                    changedAtUtc);
            }
        }
    }

    private static void ValidateMemberSpecs(
        IReadOnlyCollection<AssetBundleMemberSpec> members)
    {
        if (members.Count is < 1 or > MaximumMembers)
        {
            throw new DomainRuleException(
                "bundle_member_count_invalid",
                $"A bundle must contain between 1 and {MaximumMembers} assets.");
        }

        if (members.Select(member => member.AssetId).Distinct().Count() != members.Count)
        {
            throw new DomainRuleException(
                "bundle_duplicate_asset",
                "An asset can appear only once in a bundle.");
        }

        if (members.Any(member => member.AssetId == Guid.Empty))
        {
            throw new DomainRuleException(
                "bundle_asset_required",
                "Every bundle member must reference an asset.");
        }

        foreach (var specification in members)
        {
            AssetBundleMember.ValidateRole(
                specification.Role,
                specification.OtherRole);
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The asset bundle was changed by another user.");
        }
    }

    private static void EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Asset bundle timestamps must be UTC.");
        }
    }
}

public sealed class AssetBundleMember
{
    private AssetBundleMember()
    {
    }

    private AssetBundleMember(
        Guid id,
        Guid bundleId,
        Guid assetId,
        AssetBundleMemberRole role,
        string? otherRole,
        bool isActive,
        DateTimeOffset addedAtUtc)
    {
        Id = id;
        BundleId = bundleId;
        AssetId = assetId;
        SetRole(role, otherRole);
        IsActive = isActive;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid BundleId { get; private set; }

    public Guid AssetId { get; private set; }

    public AssetBundleMemberRole Role { get; private set; }

    public string? OtherRole { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset AddedAtUtc { get; private set; }

    public DateTimeOffset? RemovedAtUtc { get; private set; }

    internal static AssetBundleMember Create(
        Guid bundleId,
        AssetBundleMemberSpec specification,
        bool bundleIsActive,
        DateTimeOffset addedAtUtc) =>
        new(
            Guid.NewGuid(),
            bundleId,
            specification.AssetId,
            specification.Role,
            specification.OtherRole,
            bundleIsActive,
            addedAtUtc);

    internal void Include(
        AssetBundleMemberRole role,
        string? otherRole,
        bool bundleIsActive,
        DateTimeOffset includedAtUtc)
    {
        SetRole(role, otherRole);
        IsActive = bundleIsActive;
        if (RemovedAtUtc.HasValue)
        {
            AddedAtUtc = includedAtUtc;
        }

        RemovedAtUtc = null;
    }

    internal void Remove(DateTimeOffset removedAtUtc)
    {
        RemovedAtUtc = removedAtUtc;
        IsActive = false;
    }

    internal void SetBundleActive(bool isActive)
    {
        if (RemovedAtUtc is null)
        {
            IsActive = isActive;
        }
    }

    internal static void ValidateRole(
        AssetBundleMemberRole role,
        string? otherRole)
    {
        var normalizedOtherRole = TextRules.Optional(
            otherRole,
            nameof(OtherRole),
            100);
        if (role == AssetBundleMemberRole.Other && normalizedOtherRole is null)
        {
            throw new DomainRuleException(
                "bundle_other_role_required",
                "OtherRole is required when the member role is Other.");
        }

        if (role != AssetBundleMemberRole.Other && normalizedOtherRole is not null)
        {
            throw new DomainRuleException(
                "bundle_other_role_invalid",
                "OtherRole can only be supplied when the member role is Other.");
        }
    }

    private void SetRole(AssetBundleMemberRole role, string? otherRole)
    {
        ValidateRole(role, otherRole);
        Role = role;
        OtherRole = TextRules.Optional(otherRole, nameof(OtherRole), 100);
    }
}
