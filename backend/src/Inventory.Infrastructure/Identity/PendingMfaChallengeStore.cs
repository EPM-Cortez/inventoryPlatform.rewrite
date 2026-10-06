using System.Security.Cryptography;
using Inventory.Application.Common;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Identity;

public sealed class PendingMfaChallengeStore
{
    public const string CookieClaimType = "inventory:pending_mfa_challenge";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;

    public PendingMfaChallengeStore(InventoryDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<string> CreateAsync(
        Guid userId,
        string securityStamp,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = _clock.UtcNow;
        _dbContext.PendingMfaChallenges.Add(
            new PendingMfaChallenge(
                Hash(token),
                userId,
                securityStamp,
                now,
                now.Add(Lifetime)));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    public Task<bool> IsActiveAsync(
        string token,
        Guid userId,
        string securityStamp,
        CancellationToken cancellationToken) =>
        _dbContext.PendingMfaChallenges.AsNoTracking().AnyAsync(
            challenge =>
                challenge.TokenHash == Hash(token) &&
                challenge.UserId == userId &&
                challenge.SecurityStamp == securityStamp &&
                challenge.ConsumedAtUtc == null &&
                challenge.ExpiresAtUtc > _clock.UtcNow,
            cancellationToken);

    public async Task<bool> ConsumeAsync(
        string token,
        Guid userId,
        string securityStamp,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var affected = await _dbContext.PendingMfaChallenges
            .Where(
                challenge =>
                    challenge.TokenHash == Hash(token) &&
                    challenge.UserId == userId &&
                    challenge.SecurityStamp == securityStamp &&
                    challenge.ConsumedAtUtc == null &&
                    challenge.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    challenge => challenge.ConsumedAtUtc,
                    now),
                cancellationToken);
        return affected == 1;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(token)));
}
