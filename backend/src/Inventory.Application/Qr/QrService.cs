using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Common;
using Inventory.Domain.Qr;

namespace Inventory.Application.Qr;

public sealed class QrService
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IInventoryRepository _inventoryRepository;
    private readonly IQrRepository _qrRepository;
    private readonly IQrTokenProtector _tokenProtector;
    private readonly IQrCodeRenderer _renderer;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public QrService(
        IInventoryRepository inventoryRepository,
        IQrRepository qrRepository,
        IQrTokenProtector tokenProtector,
        IQrCodeRenderer renderer,
        ISchoolSettingsProvider schoolSettingsProvider,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _inventoryRepository = inventoryRepository;
        _qrRepository = qrRepository;
        _tokenProtector = tokenProtector;
        _renderer = renderer;
        _schoolSettingsProvider = schoolSettingsProvider;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<QrIdentifierView> IssueAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        if (await _qrRepository.GetActiveAsync(assetId, cancellationToken) is not null)
        {
            throw new ResourceConflictException(
                "active_qr_exists",
                "The asset already has an active QR identifier.");
        }

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var material = _tokenProtector.Create();
        var tokenVersion =
            await _qrRepository.GetLatestTokenVersionAsync(assetId, cancellationToken) + 1;
        QrIdentifier identifier;

        try
        {
            identifier = QrIdentifier.Issue(
                assetId,
                material.TokenHash,
                material.ProtectedToken,
                tokenVersion,
                now,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _qrRepository.Add(identifier);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.QrIssued,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"QR identifier version {identifier.TokenVersion} was issued for asset {asset.AssetTag}.",
                JsonSerializer.Serialize(
                    new
                    {
                        qrIdentifierId = identifier.Id,
                        identifier.TokenVersion,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await ToViewAsync(identifier, material.Token, cancellationToken);
    }

    public async Task<QrIdentifierView> RotateAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        var current = await _qrRepository.GetActiveAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Active QR identifier", assetId);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var material = _tokenProtector.Create();
        QrIdentifier replacement;

        try
        {
            current.Revoke(
                current.Version,
                now,
                actor.UserId,
                actor.DisplayName,
                "Rotated by an authorised inventory user.");
            replacement = QrIdentifier.Issue(
                assetId,
                material.TokenHash,
                material.ProtectedToken,
                checked(current.TokenVersion + 1),
                now,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var rotationEvent = AssetEvent.Create(
            asset.Id,
            AssetEventType.QrRotated,
            now,
            actor.UserId,
            actor.DisplayName,
            AssetEventSource.Api,
            $"QR identifier for asset {asset.AssetTag} was rotated to version {replacement.TokenVersion}.",
            JsonSerializer.Serialize(
                new
                {
                    previousQrIdentifierId = current.Id,
                    replacementQrIdentifierId = replacement.Id,
                    previousTokenVersion = current.TokenVersion,
                    replacementTokenVersion = replacement.TokenVersion,
                },
                EventJsonOptions),
            actor.CorrelationId);

        try
        {
            await _qrRepository.SaveRotationAsync(
                replacement,
                rotationEvent,
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }

        return await ToViewAsync(replacement, material.Token, cancellationToken);
    }

    public async Task<QrImageView> RenderAsync(
        Guid assetId,
        QrImageFormat format,
        CancellationToken cancellationToken)
    {
        _ = await RequireAssetAsync(assetId, cancellationToken);
        var identifier = await _qrRepository.GetActiveAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Active QR identifier", assetId);
        var token = _tokenProtector.Unprotect(identifier.ProtectedToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var payload = BuildPayloadUrl(settings.QrBaseUrl, token);
        var content = _renderer.Render(payload, format);

        return format switch
        {
            QrImageFormat.Svg => new QrImageView(
                content,
                "image/svg+xml; charset=utf-8",
                $"{assetId:N}-qr-v{identifier.TokenVersion}.svg"),
            QrImageFormat.Png => new QrImageView(
                content,
                "image/png",
                $"{assetId:N}-qr-v{identifier.TokenVersion}.png"),
            _ => throw new RequestValidationException(
                "invalid_qr_format",
                "QR format must be svg or png."),
        };
    }

    public async Task<string> GetPayloadUrlAsync(
        Guid assetId,
        bool issueIfMissing,
        CancellationToken cancellationToken)
    {
        _ = await RequireAssetAsync(assetId, cancellationToken);
        var identifier = await _qrRepository.GetActiveAsync(assetId, cancellationToken);
        if (identifier is null)
        {
            if (!issueIfMissing)
            {
                throw new ResourceNotFoundException("Active QR identifier", assetId);
            }

            return (await IssueAsync(assetId, cancellationToken)).PayloadUrl;
        }

        var token = _tokenProtector.Unprotect(identifier.ProtectedToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return BuildPayloadUrl(settings.QrBaseUrl, token);
    }

    public async Task<QrResolutionView> ResolveAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 200)
        {
            throw OpaqueNotFound();
        }

        var identifier = await _qrRepository.GetActiveByTokenHashAsync(
            _tokenProtector.Hash(token),
            cancellationToken);
        if (identifier is null)
        {
            throw OpaqueNotFound();
        }

        var asset = await _inventoryRepository.GetAssetViewAsync(
            identifier.AssetId,
            cancellationToken);
        if (asset is null)
        {
            throw OpaqueNotFound();
        }

        return new QrResolutionView(
            asset.Id,
            asset.AssetTag,
            asset.CategoryId,
            asset.CategoryCode,
            asset.CategoryName,
            asset.Status,
            asset.Condition,
            asset.LocationId,
            asset.LocationCode,
            asset.LocationName,
            asset.WarrantySummary);
    }

    private async Task<Asset> RequireAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        await _inventoryRepository.GetAssetAsync(assetId, cancellationToken)
        ?? throw new ResourceNotFoundException("Asset", assetId);

    private async Task<QrIdentifierView> ToViewAsync(
        QrIdentifier identifier,
        string token,
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return new QrIdentifierView(
            identifier.Id,
            identifier.AssetId,
            identifier.TokenVersion,
            identifier.IssuedAtUtc,
            BuildPayloadUrl(settings.QrBaseUrl, token),
            $"/api/v1/assets/{identifier.AssetId}/qr");
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _qrRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }
    }

    private static string BuildPayloadUrl(string baseUrl, string token) =>
        $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(token)}";

    private static ResourceNotFoundException OpaqueNotFound() =>
        new("QR identifier", "active");

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
