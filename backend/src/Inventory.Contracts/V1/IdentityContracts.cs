using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(512)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

public sealed class MfaLoginRequest
{
    [Required]
    [StringLength(20)]
    public string Code { get; init; } = string.Empty;

    public bool RememberMe { get; init; }

    public bool RememberMachine { get; init; }
}

public sealed class MfaRecoveryLoginRequest
{
    [Required]
    [StringLength(50)]
    public string RecoveryCode { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

public sealed class BeginMfaEnrollmentRequest
{
    [Required]
    [StringLength(512)]
    public string CurrentPassword { get; init; } = string.Empty;
}

public sealed class ConfirmMfaEnrollmentRequest
{
    [Required]
    [StringLength(20)]
    public string Code { get; init; } = string.Empty;
}

public sealed class RegenerateMfaRecoveryCodesRequest
{
    [Required]
    [StringLength(512)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Code { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required]
    [StringLength(512)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class CreateUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 12)]
    public string TemporaryPassword { get; init; } = string.Empty;

    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed class SetUserRolesRequest
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    [Required]
    public Guid? Version { get; init; }
}

public sealed class UpdateUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<string> Roles { get; init; } = [];

    [Required]
    public Guid? Version { get; init; }
}

public sealed class UserVersionRequest
{
    [Required]
    public Guid? Version { get; init; }
}

public sealed class ResetUserPasswordRequest
{
    [Required]
    [StringLength(512, MinimumLength = 12)]
    public string NewTemporaryPassword { get; init; } = string.Empty;

    [Required]
    public Guid? Version { get; init; }
}

public sealed record CsrfTokenResponse(string Token);

public sealed record CurrentUserResponse(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    bool MfaEnrollmentRequired);

public sealed record MfaStatusResponse(
    bool Enabled,
    bool EnrollmentRequired,
    int RecoveryCodesRemaining);

public sealed record MfaEnrollmentResponse(
    string SharedKey,
    string AuthenticatorUri);

public sealed record MfaRecoveryCodesResponse(
    IReadOnlyList<string> RecoveryCodes,
    int RecoveryCodesRemaining);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsEnabled,
    bool MustChangePassword,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset? ArchivedAtUtc,
    Guid Version);

public sealed record RolePresetResponse(
    string Name,
    IReadOnlyList<string> Permissions);
