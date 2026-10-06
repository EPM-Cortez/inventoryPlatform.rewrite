using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Inventory.Infrastructure.Identity;

public sealed class ProtectedApplicationUserStore
    : UserStore<ApplicationUser, IdentityRole<Guid>, InventoryDbContext, Guid>
{
    internal const string ProtectedValuePrefix = "dp:v1:";
    internal const string ProtectorPurpose =
        "Inventory.Identity.UserTokenStore.v1";

    private readonly IDataProtector _protector;

    public ProtectedApplicationUserStore(
        InventoryDbContext context,
        IdentityErrorDescriber describer,
        IDataProtectionProvider dataProtectionProvider)
        : base(context, describer)
    {
        _protector = dataProtectionProvider.CreateProtector(
            ProtectorPurpose);
    }

    public override Task SetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        string? value,
        CancellationToken cancellationToken)
    {
        var protectedValue = value is null
            ? null
            : ProtectedValuePrefix + _protector.Protect(value);
        return base.SetTokenAsync(
            user,
            loginProvider,
            name,
            protectedValue,
            cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        var value = await base.GetTokenAsync(
            user,
            loginProvider,
            name,
            cancellationToken);
        if (value is null ||
            !value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            return value;
        }

        return _protector.Unprotect(value[ProtectedValuePrefix.Length..]);
    }
}
