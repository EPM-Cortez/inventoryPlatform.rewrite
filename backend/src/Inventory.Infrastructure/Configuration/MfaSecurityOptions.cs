using Inventory.Application.Security;

namespace Inventory.Infrastructure.Configuration;

public sealed class MfaSecurityOptions
{
    public bool EnforceRequiredRoles { get; init; } = true;

    public IReadOnlyList<string> RequiredRoles { get; init; } = [];

    public int RecoveryCodeCount { get; init; } = 10;

    public string Issuer { get; init; } = "Inventory";

    public void Validate()
    {
        var invalidRoles = RequiredRoles
            .Where(role => !RoleNames.All.Contains(role, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (invalidRoles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Mfa:RequiredRoles contains unknown role(s): {string.Join(", ", invalidRoles)}.");
        }

        if (RecoveryCodeCount is < 5 or > 20)
        {
            throw new InvalidOperationException(
                "Mfa:RecoveryCodeCount must be between 5 and 20.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || Issuer.Trim().Length > 100)
        {
            throw new InvalidOperationException(
                "Mfa:Issuer must contain between 1 and 100 characters.");
        }
    }
}
