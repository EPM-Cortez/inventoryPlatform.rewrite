using Microsoft.AspNetCore.Identity;

namespace Inventory.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public Guid? LinkedPersonId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();
}
