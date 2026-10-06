using Inventory.Domain.Assets;
using Inventory.Domain.Qr;

namespace Inventory.Application.Abstractions;

public interface IQrRepository
{
    Task<QrIdentifier?> GetActiveAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    Task<QrIdentifier?> GetActiveByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task<int> GetLatestTokenVersionAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    void Add(QrIdentifier identifier);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task SaveRotationAsync(
        QrIdentifier replacement,
        AssetEvent rotationEvent,
        CancellationToken cancellationToken);
}

public sealed record QrTokenMaterial(
    string Token,
    string TokenHash,
    string ProtectedToken);

public interface IQrTokenProtector
{
    QrTokenMaterial Create();

    string Hash(string token);

    string Unprotect(string protectedToken);
}

public enum QrImageFormat
{
    Svg,
    Png,
}

public interface IQrCodeRenderer
{
    byte[] Render(string payload, QrImageFormat format);
}
