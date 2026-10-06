using Inventory.Application.Assets;
using Inventory.Domain.Assets;

namespace Inventory.Application.Qr;

public sealed record QrIdentifierView(
    Guid Id,
    Guid AssetId,
    int TokenVersion,
    DateTimeOffset IssuedAtUtc,
    string PayloadUrl,
    string ImageUrl);

public sealed record QrImageView(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record QrResolutionView(
    Guid Id,
    string AssetTag,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    AssetStatus Status,
    AssetCondition Condition,
    Guid? LocationId,
    string? LocationCode,
    string? LocationName,
    WarrantySummaryView WarrantySummary);
