using System.Security.Cryptography;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Inventory.Infrastructure.Qr;

public sealed class DataProtectionQrTokenProtector : IQrTokenProtector
{
    private const string Purpose = "Inventory.QrIdentifier.Token.v1";
    private readonly IDataProtector _protector;

    public DataProtectionQrTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public QrTokenMaterial Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new QrTokenMaterial(
            token,
            Hash(token),
            _protector.Protect(token));
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return WebEncoders.Base64UrlEncode(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }

    public string Unprotect(string protectedToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedToken);
        return _protector.Unprotect(protectedToken);
    }
}
