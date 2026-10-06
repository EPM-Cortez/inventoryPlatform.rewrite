namespace Inventory.Application.AssetTags;

public sealed record AssetTagPolicyView(
    string Prefix,
    int NumericWidth,
    long NextNumber,
    string? NextAssetTag,
    Guid Version);

public sealed record UpdateAssetTagPolicyCommand(
    string Prefix,
    int NumericWidth,
    long NextNumber,
    Guid Version);
