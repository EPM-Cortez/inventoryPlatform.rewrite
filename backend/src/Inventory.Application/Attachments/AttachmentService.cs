using System.Security.Cryptography;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;

namespace Inventory.Application.Attachments;

public sealed class AttachmentService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAttachmentRepository _attachmentRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IDatabaseStorageWriteGuard _storageWriteGuard;
    private readonly IAttachmentMalwareScanner _malwareScanner;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public AttachmentService(
        IAttachmentRepository attachmentRepository,
        IInventoryRepository inventoryRepository,
        IDatabaseStorageWriteGuard storageWriteGuard,
        IAttachmentMalwareScanner malwareScanner,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _attachmentRepository = attachmentRepository;
        _inventoryRepository = inventoryRepository;
        _storageWriteGuard = storageWriteGuard;
        _malwareScanner = malwareScanner;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<PageResult<AssetAttachmentView>> ListAsync(
        Guid assetId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await RequireAssetAsync(assetId, cancellationToken);
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        return await _attachmentRepository.ListAsync(
            assetId,
            includeArchived,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
    }

    public async Task<AssetAttachmentView> UploadAsync(
        Guid assetId,
        UploadAssetAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        var content = await ReadBoundedAsync(command.Content, cancellationToken);
        var validated = AttachmentFileValidator.Validate(command.FileName, content);
        var actor = _currentUser.RequireActor();
        var scan = await _malwareScanner.ScanAsync(
            validated.FileName,
            validated.ContentType,
            content,
            cancellationToken);
        if (scan.Verdict != AttachmentScanVerdict.Clean)
        {
            var attemptId = Guid.NewGuid();
            _inventoryRepository.AddAuditLog(
                CreateAudit(
                    actor,
                    "attachment.scanned",
                    attemptId,
                    JsonSerializer.Serialize(
                        new
                        {
                            attemptId,
                            assetId,
                            asset.AssetTag,
                            validated.FileName,
                            validated.ContentType,
                            validated.Sha256,
                            scan.Verdict,
                            scan.Engine,
                            scan.DetailCode,
                        },
                        JsonOptions),
                    outcome: "failed"));
            await SaveChangesAsync(cancellationToken);
            throw new ResourceConflictException(
                scan.Verdict == AttachmentScanVerdict.Rejected
                    ? "attachment_malware_detected"
                    : "attachment_scan_unavailable",
                scan.Verdict == AttachmentScanVerdict.Rejected
                    ? "The attachment was rejected by malware scanning."
                    : "Attachment scanning is unavailable. The file was not stored.");
        }

        var usage = await _attachmentRepository.GetStorageUsageAsync(
            assetId,
            cancellationToken);
        if (usage.AttachmentCount >= AssetAttachment.MaximumAttachmentsPerAsset ||
            usage.StoredBytes + content.LongLength >
            AssetAttachment.MaximumStoredBytesPerAsset)
        {
            throw new ResourceConflictException(
                "attachment_quota_exceeded",
                $"An asset can retain at most " +
                $"{AssetAttachment.MaximumAttachmentsPerAsset} attachments and " +
                $"{AssetAttachment.MaximumStoredBytesPerAsset} stored bytes.");
        }

        _storageWriteGuard.EnsureCapacity(content.LongLength);

        AssetAttachment attachment;
        try
        {
            attachment = AssetAttachment.Create(
                assetId,
                (AssetAttachmentCategory)command.Category,
                validated.FileName,
                validated.ContentType,
                content,
                validated.Sha256,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _attachmentRepository.Add(attachment);
        var details = JsonSerializer.Serialize(
            new
            {
                attachment.Id,
                attachment.Category,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.Sha256,
                scan.Verdict,
                scan.Engine,
                scan.DetailCode,
            },
            JsonOptions);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                assetId,
                AssetEventType.AttachmentAdded,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Attachment '{validated.FileName}' was added to asset {asset.AssetTag}.",
                details,
                actor.CorrelationId));
        _inventoryRepository.AddAuditLog(
            CreateAudit(
                actor,
                "attachment.uploaded",
                attachment.Id,
                details));
        await SaveChangesAsync(cancellationToken);
        return await GetViewAsync(assetId, attachment.Id, cancellationToken);
    }

    public async Task<AssetAttachmentDownload> DownloadAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        await RequireAssetAsync(assetId, cancellationToken);
        var attachment = await _attachmentRepository.GetAsync(
                assetId,
                attachmentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Asset attachment", attachmentId);
        var actor = _currentUser.RequireActor();
        var content = attachment.Content.ToArray();
        var actualHash = SHA256.HashData(content);
        byte[]? storedHash = null;
        try
        {
            storedHash = Convert.FromHexString(attachment.Sha256);
        }
        catch (FormatException)
        {
            // The integrity failure is handled below and recorded without serving content.
        }

        if (content.LongLength != attachment.SizeBytes ||
            storedHash is null ||
            storedHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(storedHash, actualHash))
        {
            _inventoryRepository.AddAuditLog(
                CreateAudit(
                    actor,
                    "attachment.downloaded",
                    attachment.Id,
                    JsonSerializer.Serialize(
                        new
                        {
                            attachment.AssetId,
                            attachment.FileName,
                            attachment.SizeBytes,
                            attachment.Sha256,
                            ActualSha256 = Convert.ToHexString(actualHash),
                            actualSizeBytes = content.LongLength,
                            reason = "content_integrity_mismatch",
                        },
                        JsonOptions),
                    outcome: "failed"));
            await SaveChangesAsync(cancellationToken);
            throw new ResourceConflictException(
                "attachment_integrity_failed",
                "The attachment failed its integrity check and was not downloaded.");
        }

        _inventoryRepository.AddAuditLog(
            CreateAudit(
                actor,
                "attachment.downloaded",
                attachment.Id,
                JsonSerializer.Serialize(
                    new
                    {
                        attachment.AssetId,
                        attachment.FileName,
                        attachment.ContentType,
                        attachment.SizeBytes,
                        attachment.Sha256,
                        attachment.ArchivedAtUtc,
                    },
                    JsonOptions)));
        await SaveChangesAsync(cancellationToken);
        return new AssetAttachmentDownload(
            attachment.FileName,
            attachment.ContentType,
            content,
            attachment.Sha256);
    }

    public async Task<AssetAttachmentView> ArchiveAsync(
        Guid assetId,
        Guid attachmentId,
        ArchiveAssetAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        var attachment = await _attachmentRepository.GetAsync(
                assetId,
                attachmentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Asset attachment", attachmentId);
        var actor = _currentUser.RequireActor();
        try
        {
            attachment.Archive(
                command.Version,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var details = JsonSerializer.Serialize(
            new
            {
                attachment.Id,
                attachment.FileName,
                attachment.Sha256,
            },
            JsonOptions);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                assetId,
                AssetEventType.AttachmentArchived,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Attachment '{attachment.FileName}' on asset {asset.AssetTag} was archived.",
                details,
                actor.CorrelationId));
        _inventoryRepository.AddAuditLog(
            CreateAudit(
                actor,
                "attachment.archived",
                attachment.Id,
                details));
        await SaveChangesAsync(cancellationToken);
        return await GetViewAsync(assetId, attachmentId, cancellationToken);
    }

    private async Task<AssetAttachmentView> GetViewAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken) =>
        await _attachmentRepository.GetViewAsync(
            assetId,
            attachmentId,
            cancellationToken)
        ?? throw new InvalidOperationException(
            $"Attachment '{attachmentId}' was not readable after it was saved.");

    private async Task<Assets.AssetView> RequireAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        await _inventoryRepository.GetAssetViewAsync(assetId, cancellationToken)
        ?? throw new ResourceNotFoundException("Asset", assetId);

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > AssetAttachment.MaximumContentBytes)
            {
                throw AttachmentValidation(
                    "attachment_too_large",
                    $"Attachments cannot exceed {AssetAttachment.MaximumContentBytes} bytes.",
                    "file");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (total == 0)
        {
            throw AttachmentValidation(
                "attachment_empty",
                "The attachment file is empty.",
                "file");
        }

        return buffer.ToArray();
    }

    private AuditLog CreateAudit(
        CurrentActor actor,
        string action,
        Guid attachmentId,
        string detailsJson,
        string outcome = "succeeded") =>
        AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            action,
            "asset_attachment",
            attachmentId.ToString(),
            _clock.UtcNow,
            outcome,
            detailsJson,
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _inventoryRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private static RequestValidationException AttachmentValidation(
        string code,
        string message,
        string field) =>
        new(
            code,
            message,
            new Dictionary<string, string[]>
            {
                [field] = [message],
            });

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code is "concurrency_conflict" or "attachment_already_archived"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
