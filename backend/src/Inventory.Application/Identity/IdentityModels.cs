using Inventory.Application.Common;

namespace Inventory.Application.Identity;

public sealed record AuthenticationResult(bool Succeeded, string? FailureCode = null);

public sealed record MfaStatusView(
    bool Enabled,
    bool EnrollmentRequired,
    int RecoveryCodesRemaining);

public sealed record MfaEnrollmentView(
    string SharedKey,
    string AuthenticatorUri);

public sealed record MfaRecoveryCodesView(
    IReadOnlyList<string> RecoveryCodes,
    int RecoveryCodesRemaining);

public sealed record UserView(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsEnabled,
    bool MustChangePassword,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset? ArchivedAtUtc,
    Guid Version);

public sealed record CreateUserCommand(
    string Email,
    string DisplayName,
    string TemporaryPassword,
    IReadOnlyCollection<string> Roles);

public sealed record UpdateUserCommand(
    string Email,
    string DisplayName,
    bool IsEnabled,
    IReadOnlyCollection<string> Roles,
    Guid Version);

public sealed record BootstrapAdminCommand(
    string Email,
    string DisplayName,
    string Password);

public interface IIdentityService
{
    Task<AuthenticationResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken);

    Task<AuthenticationResult> CompleteMfaLoginAsync(
        string code,
        bool rememberMe,
        bool rememberMachine,
        CancellationToken cancellationToken);

    Task<AuthenticationResult> CompleteMfaRecoveryLoginAsync(
        string recoveryCode,
        bool rememberMe,
        CancellationToken cancellationToken);

    Task<MfaStatusView> GetMfaStatusAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<MfaEnrollmentView> BeginMfaEnrollmentAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken);

    Task<MfaRecoveryCodesView> ConfirmMfaEnrollmentAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken);

    Task<MfaRecoveryCodesView> RegenerateMfaRecoveryCodesAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    Task<PageResult<UserView>> ListUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<UserView> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<UserView> CreateUserAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken);

    Task<UserView> UpdateUserAsync(
        Guid userId,
        UpdateUserCommand command,
        CancellationToken cancellationToken);

    Task<UserView> SetRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid version,
        CancellationToken cancellationToken);

    Task DisableUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken);

    Task ArchiveUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken);

    Task<UserView> RestoreUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        Guid userId,
        string newTemporaryPassword,
        Guid version,
        CancellationToken cancellationToken);

    Task<UserView> ResetMfaAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken);
}

public interface IRolePermissionSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

public interface IAdminBootstrapper
{
    Task BootstrapAsync(
        BootstrapAdminCommand command,
        CancellationToken cancellationToken);
}
