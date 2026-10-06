using System.Security.Claims;
using System.Text.Json;
using Inventory.Application.Common;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Domain.Auditing;
using Inventory.Infrastructure.Configuration;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, Guid, Exception?>
        LogIdentitySessionRefreshFailure = LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1001, nameof(LogIdentitySessionRefreshFailure)),
            "Identity settings changed for user {UserId}, but the session could not be refreshed; signing out.");

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly InventoryDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IdentityAdministrationLock _administrationLock;
    private readonly ILogger<IdentityService> _logger;
    private readonly MfaSecurityOptions _mfaOptions;
    private readonly IOperationalMetrics _metrics;
    private readonly PendingMfaChallengeStore _pendingMfaChallenges;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        InventoryDbContext dbContext,
        ICurrentUserContext currentUser,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IdentityAdministrationLock administrationLock,
        ILogger<IdentityService> logger,
        IOptions<MfaSecurityOptions> mfaOptions,
        IOperationalMetrics metrics,
        PendingMfaChallengeStore pendingMfaChallenges)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
        _httpContextAccessor = httpContextAccessor;
        _administrationLock = administrationLock;
        _logger = logger;
        _mfaOptions = mfaOptions.Value;
        _metrics = metrics;
        _pendingMfaChallenges = pendingMfaChallenges;
    }

    public async Task<AuthenticationResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByEmailAsync(normalizedEmail);

        if (user is null || !user.IsEnabled || user.ArchivedAtUtc.HasValue)
        {
            _metrics.RecordFailedLogin();
            await WriteLoginAuditAsync(user, "failed", "invalid_credentials", cancellationToken);
            return new AuthenticationResult(false, "invalid_credentials");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await _signInManager.PasswordSignInAsync(
                user,
                password,
                rememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await WriteLoginAuditAsync(user, "succeeded", null, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AuthenticationResult(true);
            }

            var failureCode = result.RequiresTwoFactor
                ? "mfa_required"
                : result.IsLockedOut
                    ? "locked_out"
                    : "invalid_credentials";

            if (!result.RequiresTwoFactor)
            {
                _metrics.RecordFailedLogin();
            }

            await WriteLoginAuditAsync(user, "failed", failureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthenticationResult(false, failureCode);
        }
        catch
        {
            await _signInManager.SignOutAsync();
            throw;
        }
    }

    public async Task<AuthenticationResult> CompleteMfaLoginAsync(
        string code,
        bool rememberMe,
        bool rememberMachine,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsEnabled || user.ArchivedAtUtc.HasValue)
        {
            _metrics.RecordFailedMfaChallenge();
            await WriteLoginAuditAsync(user, "failed", "invalid_mfa_session", cancellationToken);
            return new AuthenticationResult(false, "invalid_mfa_session");
        }

        var challenge = await GetPendingMfaChallengeAsync(user, cancellationToken);
        if (challenge is null)
        {
            return await RejectInvalidMfaSessionAsync(user, cancellationToken);
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _userManager.IsLockedOutAsync(user))
            {
                _metrics.RecordFailedMfaChallenge();
                await WriteLoginAuditAsync(user, "failed", "locked_out", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AuthenticationResult(false, "locked_out");
            }

            if (!await _signInManager.CanSignInAsync(user) ||
                !await _signInManager.IsTwoFactorEnabledAsync(user))
            {
                await transaction.RollbackAsync(cancellationToken);
                return await RejectInvalidMfaSessionAsync(user, cancellationToken);
            }

            var normalizedCode = code.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _signInManager.Options.Tokens.AuthenticatorTokenProvider,
                    normalizedCode))
            {
                if (!await _pendingMfaChallenges.ConsumeAsync(
                        challenge,
                        user.Id,
                        await _userManager.GetSecurityStampAsync(user),
                        cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return await RejectInvalidMfaSessionAsync(user, cancellationToken);
                }

                ThrowIfFailed(
                    await _userManager.ResetAccessFailedCountAsync(user),
                    "reset MFA login failures");
                await WriteLoginAuditAsync(user, "succeeded", "mfa", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await CompleteMfaSignInAsync(user, rememberMe, rememberMachine);
                return new AuthenticationResult(true);
            }

            var failed = await _userManager.AccessFailedAsync(user);
            var failureCode = failed.Succeeded && await _userManager.IsLockedOutAsync(user)
                ? "locked_out"
                : "invalid_mfa_code";
            _metrics.RecordFailedMfaChallenge();
            await WriteLoginAuditAsync(user, "failed", failureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthenticationResult(false, failureCode);
        }
        catch
        {
            await _signInManager.SignOutAsync();
            throw;
        }
    }

    public async Task<AuthenticationResult> CompleteMfaRecoveryLoginAsync(
        string recoveryCode,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsEnabled || user.ArchivedAtUtc.HasValue)
        {
            _metrics.RecordFailedMfaChallenge();
            await WriteLoginAuditAsync(
                user,
                "failed",
                "invalid_mfa_session",
                cancellationToken);
            return new AuthenticationResult(false, "invalid_mfa_session");
        }

        var challenge = await GetPendingMfaChallengeAsync(user, cancellationToken);
        if (challenge is null)
        {
            return await RejectInvalidMfaSessionAsync(user, cancellationToken);
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _userManager.IsLockedOutAsync(user))
            {
                _metrics.RecordFailedMfaChallenge();
                await WriteLoginAuditAsync(user, "failed", "locked_out", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AuthenticationResult(false, "locked_out");
            }

            if (!await _signInManager.CanSignInAsync(user) ||
                !await _signInManager.IsTwoFactorEnabledAsync(user))
            {
                await transaction.RollbackAsync(cancellationToken);
                return await RejectInvalidMfaSessionAsync(user, cancellationToken);
            }

            var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                recoveryCode.Trim());
            if (result.Succeeded)
            {
                if (!await _pendingMfaChallenges.ConsumeAsync(
                        challenge,
                        user.Id,
                        await _userManager.GetSecurityStampAsync(user),
                        cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return await RejectInvalidMfaSessionAsync(user, cancellationToken);
                }

                ThrowIfFailed(
                    await _userManager.ResetAccessFailedCountAsync(user),
                    "reset MFA login failures");
                await WriteLoginAuditAsync(
                    user,
                    "succeeded",
                    "mfa_recovery_code",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await CompleteMfaSignInAsync(
                    user,
                    rememberMe: false,
                    rememberMachine: false);
                return new AuthenticationResult(true);
            }

            ThrowIfFailed(
                await _userManager.AccessFailedAsync(user),
                "record a failed recovery-code attempt");

            var failureCode = await _userManager.IsLockedOutAsync(user)
                ? "locked_out"
                : "invalid_mfa_recovery_code";
            _metrics.RecordFailedMfaChallenge();
            await WriteLoginAuditAsync(user, "failed", failureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthenticationResult(false, failureCode);
        }
        catch
        {
            await _signInManager.SignOutAsync();
            throw;
        }
    }

    private async Task<string?> GetPendingMfaChallengeAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An HTTP context is required for MFA.");
        var ticket = await context.AuthenticateAsync(
            IdentityConstants.TwoFactorUserIdScheme);
        var token = ticket.Principal?.FindFirstValue(
            PendingMfaChallengeStore.CookieClaimType);
        var stamp = ticket.Principal?.FindFirstValue(
            _signInManager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(stamp) ||
            !await _pendingMfaChallenges.IsActiveAsync(
                token,
                user.Id,
                stamp,
                cancellationToken))
        {
            return null;
        }

        return token;
    }

    private async Task<AuthenticationResult> RejectInvalidMfaSessionAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        _metrics.RecordFailedMfaChallenge();
        await WriteLoginAuditAsync(
            user,
            "failed",
            "invalid_mfa_session",
            cancellationToken);
        return new AuthenticationResult(false, "invalid_mfa_session");
    }

    private async Task CompleteMfaSignInAsync(
        ApplicationUser user,
        bool rememberMe,
        bool rememberMachine)
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An HTTP context is required for MFA.");
        await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (rememberMachine)
        {
            await _signInManager.RememberTwoFactorClientAsync(user);
        }

        await _signInManager.SignInWithClaimsAsync(
            user,
            rememberMe,
            [new Claim("amr", "mfa")]);
    }

    public async Task<MfaStatusView> GetMfaStatusAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        var roles = await _userManager.GetRolesAsync(user);
        return new MfaStatusView(
            user.TwoFactorEnabled,
            RequiresMfaEnrollment(user, roles),
            user.TwoFactorEnabled
                ? await _userManager.CountRecoveryCodesAsync(user)
                : 0);
    }

    public async Task<MfaEnrollmentView> BeginMfaEnrollmentAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        EnsureNotArchived(user);
        if (user.TwoFactorEnabled)
        {
            throw new ResourceConflictException(
                "mfa_already_enabled",
                "Multi-factor authentication is already enabled.");
        }

        await EnsureMfaSelfServiceNotLockedAsync(user);

        if (!await _userManager.CheckPasswordAsync(user, currentPassword))
        {
            await RecordFailedMfaSelfServiceAsync(
                user,
                "user.mfa.enrollment.started",
                "invalid_password",
                cancellationToken);
            throw InvalidMfaVerification();
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(
            await _userManager.ResetAuthenticatorKeyAsync(user),
            "reset the authenticator key");
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Identity did not issue an authenticator key.");
        }

        _dbContext.AuditLogs.Add(
            CreateAudit(
                user.Id,
                user.DisplayName,
                "user.mfa.enrollment.started",
                "user",
                user.Id.ToString(),
                "succeeded"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RefreshSessionAfterIdentityChangeAsync(user);

        var issuer = _mfaOptions.Issuer.Trim();
        var accountName = user.Email ?? user.UserName ?? user.Id.ToString();
        var authenticatorUri =
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:" +
            $"{Uri.EscapeDataString(accountName)}?secret={Uri.EscapeDataString(key)}&" +
            $"issuer={Uri.EscapeDataString(issuer)}&digits=6";
        return new MfaEnrollmentView(key, authenticatorUri);
    }

    public async Task<MfaRecoveryCodesView> ConfirmMfaEnrollmentAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        EnsureNotArchived(user);
        if (user.TwoFactorEnabled)
        {
            throw new ResourceConflictException(
                "mfa_already_enabled",
                "Multi-factor authentication is already enabled.");
        }

        await EnsureMfaSelfServiceNotLockedAsync(user);

        var validCode = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            NormalizeMfaCode(code));
        if (!validCode)
        {
            await RecordFailedMfaSelfServiceAsync(
                user,
                "user.mfa.enrollment.confirmed",
                "invalid_mfa_code",
                cancellationToken);
            throw InvalidMfaVerification();
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(
            await _userManager.SetTwoFactorEnabledAsync(user, enabled: true),
            "enable multi-factor authentication");
        var recoveryCodes = (
                await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                    user,
                    _mfaOptions.RecoveryCodeCount))
            ?.ToArray()
            ?? throw new InvalidOperationException(
                "Identity did not issue recovery codes.");
        user.Version = Guid.NewGuid();
        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");

        _dbContext.AuditLogs.Add(
            CreateAudit(
                user.Id,
                user.DisplayName,
                "user.mfa.enrollment.confirmed",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { RecoveryCodeCount = recoveryCodes.Length }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RefreshSessionAfterIdentityChangeAsync(user);
        return new MfaRecoveryCodesView(recoveryCodes, recoveryCodes.Length);
    }

    public async Task<MfaRecoveryCodesView> RegenerateMfaRecoveryCodesAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        EnsureNotArchived(user);
        if (!user.TwoFactorEnabled)
        {
            throw new ResourceConflictException(
                "mfa_not_enabled",
                "Multi-factor authentication is not enabled.");
        }

        await EnsureMfaSelfServiceNotLockedAsync(user);

        var verified =
            await _userManager.CheckPasswordAsync(user, currentPassword) &&
            await _userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                NormalizeMfaCode(code));
        if (!verified)
        {
            await RecordFailedMfaSelfServiceAsync(
                user,
                "user.mfa.recovery_codes.regenerated",
                "invalid_mfa_verification",
                cancellationToken);
            throw InvalidMfaVerification();
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var recoveryCodes = (
                await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                    user,
                    _mfaOptions.RecoveryCodeCount))
            ?.ToArray()
            ?? throw new InvalidOperationException(
                "Identity did not issue recovery codes.");
        user.Version = Guid.NewGuid();
        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");
        _dbContext.AuditLogs.Add(
            CreateAudit(
                user.Id,
                user.DisplayName,
                "user.mfa.recovery_codes.regenerated",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { RecoveryCodeCount = recoveryCodes.Length }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RefreshSessionAfterIdentityChangeAsync(user);
        return new MfaRecoveryCodesView(recoveryCodes, recoveryCodes.Length);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _currentUser.IsAuthenticated ? _currentUser.RequireActor() : null;

        if (actor is not null)
        {
            _dbContext.AuditLogs.Add(
                CreateAudit(
                    actor.UserId,
                    actor.DisplayName,
                    "authentication.logout",
                    "user",
                    actor.UserId.ToString(),
                    "succeeded"));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _signInManager.SignOutAsync();
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        var previousMustChange = user.MustChangePassword;
        user.MustChangePassword = false;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await _userManager.ChangePasswordAsync(
                user,
                currentPassword,
                newPassword);
        if (!result.Succeeded)
        {
            user.MustChangePassword = previousMustChange;
            throw IdentityFailure("change the password", result);
        }

        _dbContext.AuditLogs.Add(
            CreateAudit(
                user.Id,
                user.DisplayName,
                "user.password.changed",
                "user",
                user.Id.ToString(),
                "succeeded"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RefreshSessionAfterIdentityChangeAsync(user);
    }

    public async Task<PageResult<UserView>> ListUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(page, pageSize).Normalize();
        var query = _userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var users = await query
            .Skip((pageRequest.Page - 1) * pageRequest.PageSize)
            .Take(pageRequest.PageSize)
            .ToArrayAsync(cancellationToken);

        var userIds = users.Select(user => user.Id).ToArray();
        var roleAssignments = await (
                from userRole in _dbContext.UserRoles.AsNoTracking()
                join role in _dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToArrayAsync(cancellationToken);
        var rolesByUser = roleAssignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(assignment => assignment.Name!)
                    .Where(name => name is not null)
                    .ToArray());

        var items = users
            .Select(user => ToView(
                user,
                rolesByUser.GetValueOrDefault(user.Id, [])))
            .ToArray();
        return new PageResult<UserView>(
            items,
            pageRequest.Page,
            pageRequest.PageSize,
            totalCount);
    }

    public async Task<UserView> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);
        var roles = await _userManager.GetRolesAsync(user);
        return ToView(user, roles.ToArray());
    }

    public async Task<UserView> CreateUserAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ValidateRoles(command.Roles);
        ValidateUserProfile(command.Email, command.DisplayName);
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var email = command.Email.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            throw new ResourceConflictException(
                "duplicate_user_email",
                "A user with that email address already exists.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = command.DisplayName.Trim(),
            EmailConfirmed = true,
            IsEnabled = true,
            MustChangePassword = true,
            CreatedAtUtc = _clock.UtcNow,
            Version = Guid.NewGuid(),
        };

        var createResult = await _userManager.CreateAsync(user, command.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            throw IdentityFailure("create the user", createResult);
        }

        var distinctRoles = command.Roles.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctRoles.Length > 0)
        {
            var roleResult = await _userManager.AddToRolesAsync(user, distinctRoles);
            if (!roleResult.Succeeded)
            {
                throw IdentityFailure("assign the requested roles", roleResult);
            }
        }

        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                "user.created",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { user.Email, Roles = distinctRoles }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToView(user, distinctRoles);
    }

    public Task<UserView> UpdateUserAsync(
        Guid userId,
        UpdateUserCommand command,
        CancellationToken cancellationToken) =>
        UpdateUserCoreAsync(
            userId,
            command,
            "user.updated",
            cancellationToken);

    public async Task<UserView> SetRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid version,
        CancellationToken cancellationToken)
    {
        var current = await GetUserAsync(userId, cancellationToken);
        return await UpdateUserCoreAsync(
            userId,
            new UpdateUserCommand(
                current.Email,
                current.DisplayName,
                current.IsEnabled,
                roles,
                version),
            "user.roles.changed",
            cancellationToken);
    }

    public async Task DisableUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var current = await GetUserAsync(userId, cancellationToken);
        _ = await UpdateUserCoreAsync(
            userId,
            new UpdateUserCommand(
                current.Email,
                current.DisplayName,
                IsEnabled: false,
                current.Roles,
                version),
            "user.disabled",
            cancellationToken);
    }

    public async Task ArchiveUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken)
    {
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        if (actor.UserId == userId)
        {
            throw new ResourceConflictException(
                "cannot_archive_self",
                "Administrators cannot archive their own account.");
        }

        var user = await RequireUserAsync(userId);
        EnsureVersion(user, version);
        if (user.ArchivedAtUtc.HasValue)
        {
            return;
        }

        if (await _userManager.IsInRoleAsync(user, RoleNames.Administrator))
        {
            await EnsureAnotherEnabledAdministratorAsync(user.Id);
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.IsEnabled = false;
        user.ArchivedAtUtc = _clock.UtcNow;
        user.Version = Guid.NewGuid();
        ThrowIfFailed(await _userManager.UpdateAsync(user), "archive the user");
        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");

        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                "user.archived",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { user.Email, user.ArchivedAtUtc }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UserView> RestoreUserAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken)
    {
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var user = await RequireUserAsync(userId);
        EnsureVersion(user, version);
        var roles = await _userManager.GetRolesAsync(user);
        if (!user.ArchivedAtUtc.HasValue)
        {
            return ToView(user, roles.ToArray());
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.IsEnabled = true;
        user.ArchivedAtUtc = null;
        user.Version = Guid.NewGuid();
        ThrowIfFailed(await _userManager.UpdateAsync(user), "restore the user");
        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");

        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                "user.restored",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { user.Email }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(user, roles.ToArray());
    }

    public async Task ResetPasswordAsync(
        Guid userId,
        string newTemporaryPassword,
        Guid version,
        CancellationToken cancellationToken)
    {
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var user = await RequireUserAsync(userId);
        EnsureVersion(user, version);
        EnsureNotArchived(user);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.MustChangePassword = true;
        user.Version = Guid.NewGuid();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newTemporaryPassword);
        if (!result.Succeeded)
        {
            throw IdentityFailure("reset the password", result);
        }

        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                "user.password.reset",
                "user",
                user.Id.ToString(),
                "succeeded"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UserView> ResetMfaAsync(
        Guid userId,
        Guid version,
        CancellationToken cancellationToken)
    {
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var user = await RequireUserAsync(userId);
        EnsureVersion(user, version);
        EnsureNotArchived(user);
        var roles = await _userManager.GetRolesAsync(user);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(
            await _userManager.SetTwoFactorEnabledAsync(user, enabled: false),
            "disable multi-factor authentication");
        ThrowIfFailed(
            await _userManager.ResetAuthenticatorKeyAsync(user),
            "reset the authenticator key");
        user.Version = Guid.NewGuid();
        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");
        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                "user.mfa.reset",
                "user",
                user.Id.ToString(),
                "succeeded",
                new { user.Email }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (actor.UserId == user.Id)
        {
            await _signInManager.SignOutAsync();
        }

        return ToView(user, roles.ToArray());
    }

    private async Task<UserView> UpdateUserCoreAsync(
        Guid userId,
        UpdateUserCommand command,
        string auditAction,
        CancellationToken cancellationToken)
    {
        ValidateRoles(command.Roles);
        ValidateUserProfile(command.Email, command.DisplayName);
        await using var administrationLease =
            await _administrationLock.AcquireAsync(cancellationToken);
        var actor = _currentUser.RequireActor();
        var user = await RequireUserAsync(userId);
        EnsureVersion(user, command.Version);
        EnsureNotArchived(user);

        if (actor.UserId == userId && !command.IsEnabled)
        {
            throw new ResourceConflictException(
                "cannot_disable_self",
                "Administrators cannot disable their own account.");
        }

        var email = command.Email.Trim();
        var displayName = command.DisplayName.Trim();
        var duplicate = await _userManager.FindByEmailAsync(email);
        if (duplicate is not null && duplicate.Id != user.Id)
        {
            throw new ResourceConflictException(
                "duplicate_user_email",
                "A user with that email address already exists.");
        }

        var existingRoles = await _userManager.GetRolesAsync(user);
        var desiredRoles = command.Roles
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var wasAdministrator =
            existingRoles.Contains(RoleNames.Administrator, StringComparer.Ordinal);
        var willBeEnabledAdministrator =
            command.IsEnabled &&
            desiredRoles.Contains(RoleNames.Administrator, StringComparer.Ordinal);
        if (wasAdministrator && !willBeEnabledAdministrator)
        {
            await EnsureAnotherEnabledAdministratorAsync(user.Id);
        }

        var before = new
        {
            user.Email,
            user.DisplayName,
            user.IsEnabled,
            Roles = existingRoles.Order(StringComparer.Ordinal).ToArray(),
        };

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.Email = email;
        user.UserName = email;
        user.EmailConfirmed = true;
        user.DisplayName = displayName;
        user.IsEnabled = command.IsEnabled;
        user.Version = Guid.NewGuid();
        ThrowIfFailed(await _userManager.UpdateAsync(user), "update the user");

        var toRemove = existingRoles.Except(desiredRoles, StringComparer.Ordinal).ToArray();
        var toAdd = desiredRoles.Except(existingRoles, StringComparer.Ordinal).ToArray();
        if (toRemove.Length > 0)
        {
            ThrowIfFailed(
                await _userManager.RemoveFromRolesAsync(user, toRemove),
                "remove existing roles");
        }

        if (toAdd.Length > 0)
        {
            ThrowIfFailed(
                await _userManager.AddToRolesAsync(user, toAdd),
                "add requested roles");
        }

        ThrowIfFailed(
            await _userManager.UpdateSecurityStampAsync(user),
            "invalidate existing sessions");

        var after = new
        {
            user.Email,
            user.DisplayName,
            user.IsEnabled,
            Roles = desiredRoles,
        };
        _dbContext.AuditLogs.Add(
            CreateAudit(
                actor.UserId,
                actor.DisplayName,
                auditAction,
                "user",
                user.Id.ToString(),
                "succeeded",
                new { Before = before, After = after }));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToView(user, desiredRoles);
    }

    private async Task<ApplicationUser> RequireUserAsync(Guid userId) =>
        await _userManager.FindByIdAsync(userId.ToString())
        ?? throw new ResourceNotFoundException("User", userId);

    private static void EnsureVersion(ApplicationUser user, Guid version)
    {
        if (user.Version != version)
        {
            throw new ResourceConflictException(
                "concurrency_conflict",
                "The user account changed after it was loaded. Refresh and try again.");
        }
    }

    private static void EnsureNotArchived(ApplicationUser user)
    {
        if (user.ArchivedAtUtc.HasValue)
        {
            throw new ResourceConflictException(
                "user_archived",
                "Restore the archived user before changing the account.");
        }
    }

    private async Task EnsureAnotherEnabledAdministratorAsync(Guid excludingUserId)
    {
        var administrators = await _userManager.GetUsersInRoleAsync(RoleNames.Administrator);
        if (!administrators.Any(
                user =>
                    user.Id != excludingUserId &&
                    user.IsEnabled &&
                    !user.ArchivedAtUtc.HasValue))
        {
            throw new ResourceConflictException(
                "last_administrator",
                "The final enabled administrator cannot be removed or disabled.");
        }
    }

    private async Task WriteLoginAuditAsync(
        ApplicationUser? user,
        string outcome,
        string? reason,
        CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(
            CreateAudit(
                user?.Id,
                user?.DisplayName,
                "authentication.login",
                "user",
                user?.Id.ToString(),
                outcome,
                reason is null ? null : new { reason }));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteMfaAuditAsync(
        ApplicationUser user,
        string action,
        string outcome,
        string reason,
        CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(
            CreateAudit(
                user.Id,
                user.DisplayName,
                action,
                "user",
                user.Id.ToString(),
                outcome,
                new { reason }));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureMfaSelfServiceNotLockedAsync(
        ApplicationUser user)
    {
        if (await _userManager.IsLockedOutAsync(user))
        {
            throw new ResourceConflictException(
                "account_locked_out",
                "The account is temporarily locked. Try again later.");
        }
    }

    private async Task RecordFailedMfaSelfServiceAsync(
        ApplicationUser user,
        string action,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(
            await _userManager.AccessFailedAsync(user),
            "record a failed MFA verification attempt");
        await WriteMfaAuditAsync(
            user,
            action,
            "failed",
            reason,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _metrics.RecordFailedMfaChallenge();
    }

    private async Task RefreshSessionAfterIdentityChangeAsync(ApplicationUser user)
    {
        try
        {
            await _signInManager.RefreshSignInAsync(user);
        }
        catch (Exception exception)
        {
            LogIdentitySessionRefreshFailure(_logger, user.Id, exception);
            await _signInManager.SignOutAsync();
        }
    }

    private bool RequiresMfaEnrollment(
        ApplicationUser user,
        IEnumerable<string> roles) =>
        _mfaOptions.EnforceRequiredRoles &&
        !user.TwoFactorEnabled &&
        roles.Any(
            role => _mfaOptions.RequiredRoles.Contains(
                role,
                StringComparer.Ordinal));

    private static string NormalizeMfaCode(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static RequestValidationException InvalidMfaVerification() =>
        new(
            "invalid_mfa_verification",
            "The password or authenticator code was not accepted.",
            new Dictionary<string, string[]>
            {
                ["verification"] =
                [
                    "The supplied verification details were not accepted.",
                ],
            });

    private AuditLog CreateAudit(
        Guid? actorUserId,
        string? actorDisplayName,
        string action,
        string targetType,
        string? targetId,
        string outcome,
        object? details = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        return AuditLog.Create(
            actorUserId,
            actorDisplayName,
            action,
            targetType,
            targetId,
            _clock.UtcNow,
            outcome,
            details is null ? null : JsonSerializer.Serialize(details, AuditJsonOptions),
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext?.Request.Headers.UserAgent.ToString(),
            httpContext?.TraceIdentifier);
    }

    private static UserView ToView(ApplicationUser user, IReadOnlyCollection<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.IsEnabled,
            user.MustChangePassword,
            user.TwoFactorEnabled,
            roles.Order(StringComparer.Ordinal).ToArray(),
            user.ArchivedAtUtc,
            user.Version);

    private static void ValidateUserProfile(string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName))
        {
            throw new RequestValidationException(
                "invalid_user_profile",
                "Email and display name are required.");
        }
    }

    private static void ValidateRoles(IReadOnlyCollection<string> roles)
    {
        var invalidRoles = roles
            .Where(role => !RoleNames.All.Contains(role, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (invalidRoles.Length > 0)
        {
            throw new RequestValidationException(
                "invalid_role",
                $"Unknown role preset(s): {string.Join(", ", invalidRoles)}.");
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw IdentityFailure(action, result);
        }
    }

    private static RequestValidationException IdentityFailure(
        string action,
        IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);
        return new RequestValidationException(
            "identity_operation_failed",
            $"Unable to {action}.",
            errors);
    }
}
