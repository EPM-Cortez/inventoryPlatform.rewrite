using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.Common;
using Inventory.Application.Qr;
using Inventory.Domain.Auditing;

namespace Inventory.Application.Labels;

public sealed class LabelService
{
    public const int MaximumLabelsPerRequest = 240;

    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IInventoryRepository _inventoryRepository;
    private readonly QrService _qrService;
    private readonly IAssetLabelPdfRenderer _renderer;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public LabelService(
        IInventoryRepository inventoryRepository,
        QrService qrService,
        IAssetLabelPdfRenderer renderer,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _inventoryRepository = inventoryRepository;
        _qrService = qrService;
        _renderer = renderer;
        _clock = clock;
        _currentUser = currentUser;
    }

    public static IReadOnlyList<LabelStockView> ListStocks() => LabelStockCatalog.List();

    public async Task<LabelPdfDocument> PrintAsync(
        PrintLabelsCommand command,
        string fileName,
        CancellationToken cancellationToken)
    {
        Validate(command);
        if (command.IssueMissingQr &&
            !_currentUser.Permissions.Contains(
                Security.Permissions.QrGenerate,
                StringComparer.Ordinal))
        {
            throw new PermissionDeniedException(
                "qr_generate_required",
                "Issuing missing QR identifiers during label printing requires the " +
                "qr.generate permission.");
        }

        var requestedAssets = new List<(AssetView Asset, int Quantity)>(
            command.Items.Count);
        foreach (var item in command.Items)
        {
            var asset = await _inventoryRepository.GetAssetViewAsync(
                item.AssetId,
                cancellationToken)
                ?? throw new ResourceNotFoundException("Asset", item.AssetId);
            requestedAssets.Add((asset, item.Quantity));
        }

        var payloadByAsset = new Dictionary<Guid, string>();
        foreach (var (asset, _) in requestedAssets)
        {
            if (payloadByAsset.ContainsKey(asset.Id))
            {
                continue;
            }

            payloadByAsset[asset.Id] = await _qrService.GetPayloadUrlAsync(
                asset.Id,
                command.IssueMissingQr,
                cancellationToken);
        }

        var labels = new List<AssetLabelContent>(
            requestedAssets.Sum(request => request.Quantity));
        foreach (var (asset, quantity) in requestedAssets)
        {
            var content = new AssetLabelContent(
                asset.Id,
                asset.AssetTag,
                BuildDeviceName(asset),
                payloadByAsset[asset.Id]);
            for (var copy = 0; copy < quantity; copy++)
            {
                labels.Add(content);
            }
        }

        var document = _renderer.Render(
            command.Stock,
            command.StartPosition,
            labels,
            fileName);
        await RecordPrintAuditAsync(command, document, labels, cancellationToken);
        return document;
    }

    public static string BuildDeviceName(AssetView asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.DeviceName))
        {
            return asset.DeviceName.Trim();
        }

        var manufacturer = asset.Manufacturer?.Trim();
        var model = asset.Model?.Trim();
        var combined = string.Join(
            " ",
            new[] { manufacturer, model }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(combined)
            ? asset.CategoryName
            : combined;
    }

    private static void Validate(PrintLabelsCommand command)
    {
        if (!Enum.IsDefined(command.Stock))
        {
            throw Validation(
                "stock",
                "A supported label stock is required.");
        }

        if (command.Items.Count == 0)
        {
            throw Validation(
                "items",
                "At least one asset label is required.");
        }

        if (command.Items.Any(item => item.AssetId == Guid.Empty))
        {
            throw Validation(
                "items",
                "Every label item must contain an asset ID.");
        }

        if (command.Items.Any(item => item.Quantity is < 1 or > 100))
        {
            throw Validation(
                "items",
                "Each label quantity must be between 1 and 100.");
        }

        var total = command.Items.Sum(item => (long)item.Quantity);
        if (total > MaximumLabelsPerRequest)
        {
            throw Validation(
                "items",
                $"A label request cannot exceed {MaximumLabelsPerRequest} labels.");
        }

        var stock = LabelStockCatalog.Get(command.Stock);
        var maximumStart = stock.SupportsStartPosition
            ? stock.LabelsPerPage
            : 1;
        if (command.StartPosition < 1 || command.StartPosition > maximumStart)
        {
            throw Validation(
                "startPosition",
                stock.SupportsStartPosition
                    ? $"Start position must be between 1 and {maximumStart} for this stock."
                    : "Start position must be 1 for roll labels.");
        }
    }

    private async Task RecordPrintAuditAsync(
        PrintLabelsCommand command,
        LabelPdfDocument document,
        IReadOnlyList<AssetLabelContent> labels,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var assetIds = labels
            .Select(label => label.AssetId)
            .Distinct()
            .Select(id => id.ToString())
            .ToArray();
        _inventoryRepository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "qr.labels.printed",
                "asset_label_batch",
                null,
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        stock = command.Stock.ToString(),
                        document.LabelCount,
                        document.PageCount,
                        command.StartPosition,
                        command.IssueMissingQr,
                        assetIds,
                    },
                    AuditJsonOptions),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));

        try
        {
            await _inventoryRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }
    }

    private static RequestValidationException Validation(
        string field,
        string message) =>
        new(
            "invalid_label_request",
            message,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            });
}
