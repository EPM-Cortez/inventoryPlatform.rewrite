using Inventory.Domain.Agents;
using Inventory.Application.Abstractions;
using Inventory.Domain.AssetTags;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;
using Inventory.Domain.Qr;
using Inventory.Domain.School;
using Inventory.Domain.Stocktakes;
using Inventory.Domain.Warranties;
using Inventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inventory.Infrastructure.Data;

public sealed class InventoryDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcTimestampConverter =
        new(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtcTimestampConverter =
        new(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null);

    private static readonly ValueConverter<decimal?, long?> PurchaseCostMinorUnitsConverter =
        new(
            value => value.HasValue ? (long?)(value.Value * 100m) : null,
            value => value.HasValue ? value.Value / 100m : null);

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();

    public DbSet<QrIdentifier> QrIdentifiers => Set<QrIdentifier>();

    public DbSet<WarrantyCoverage> WarrantyCoverages => Set<WarrantyCoverage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<PendingMfaChallenge> PendingMfaChallenges =>
        Set<PendingMfaChallenge>();

    public DbSet<AssetImportJob> AssetImportJobs => Set<AssetImportJob>();

    public DbSet<ReferenceDataImportJob> ReferenceDataImportJobs =>
        Set<ReferenceDataImportJob>();

    public DbSet<LegacyReconciliationRun> LegacyReconciliationRuns =>
        Set<LegacyReconciliationRun>();

    public DbSet<LegacyReconciliationSignOff> LegacyReconciliationSignOffs =>
        Set<LegacyReconciliationSignOff>();

    public DbSet<ImportPreviewReservation> ImportPreviewReservations =>
        Set<ImportPreviewReservation>();

    public DbSet<ImportPreviewBudgetGate> ImportPreviewBudgetGates =>
        Set<ImportPreviewBudgetGate>();

    public DbSet<SchoolProfile> SchoolProfiles => Set<SchoolProfile>();

    public DbSet<AgentEnrollmentProfile> AgentEnrollmentProfiles =>
        Set<AgentEnrollmentProfile>();

    public DbSet<ManagedAgent> ManagedAgents => Set<ManagedAgent>();

    public DbSet<AgentKeyRecoveryAuthorization> AgentKeyRecoveryAuthorizations =>
        Set<AgentKeyRecoveryAuthorization>();

    public DbSet<ManagedAgentSnapshot> ManagedAgentSnapshots =>
        Set<ManagedAgentSnapshot>();

    public DbSet<AgentRequestNonce> AgentRequestNonces =>
        Set<AgentRequestNonce>();

    public DbSet<AssetTagPolicy> AssetTagPolicies => Set<AssetTagPolicy>();

    public DbSet<AssetBulkChangeJob> AssetBulkChangeJobs =>
        Set<AssetBulkChangeJob>();

    public DbSet<AssetBundle> AssetBundles => Set<AssetBundle>();

    public DbSet<AssetBundleMember> AssetBundleMembers =>
        Set<AssetBundleMember>();

    public DbSet<StocktakeSession> StocktakeSessions =>
        Set<StocktakeSession>();

    public DbSet<StocktakeExpectedAsset> StocktakeExpectedAssets =>
        Set<StocktakeExpectedAsset>();

    public DbSet<StocktakeObservation> StocktakeObservations =>
        Set<StocktakeObservation>();

    public DbSet<LeaverCase> LeaverCases => Set<LeaverCase>();

    public DbSet<AssetAttachment> AssetAttachments =>
        Set<AssetAttachment>();

    public DbSet<SavedAssetSearch> SavedAssetSearches =>
        Set<SavedAssetSearch>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var isPostgres = Database.IsNpgsql();

        ConfigureIdentity(builder, isPostgres);
        ConfigurePendingMfaChallenges(builder);
        ConfigureAssets(builder, isPostgres);
        ConfigureReferenceData(builder);
        ConfigurePeopleAndAssignments(builder);
        ConfigureQrAndWarranties(builder, isPostgres);
        ConfigureImports(builder);
        ConfigureImportPreviewBudget(builder);
        ConfigureLegacyReconciliation(builder);
        ConfigureAgents(builder);
        ConfigureHistoryAndAudit(builder);
        ConfigureSchoolProfile(builder);
        builder.ConfigureAssetOperations(isPostgres);
        builder.ConfigureStocktakes();
        Milestone7CustodyModelConfiguration.Configure(builder, isPostgres);
    }

    private static void ConfigurePendingMfaChallenges(ModelBuilder builder)
    {
        builder.Entity<PendingMfaChallenge>(entity =>
        {
            entity.ToTable("PendingMfaChallenges", table =>
                table.HasCheckConstraint(
                    "CK_PendingMfaChallenges_Expiry",
                    "\"ExpiresAtUtc\" > \"CreatedAtUtc\""));
            entity.HasKey(challenge => challenge.TokenHash);
            entity.Property(challenge => challenge.TokenHash)
                .HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(challenge => challenge.SecurityStamp)
                .HasMaxLength(256).IsRequired();
            entity.Property(challenge => challenge.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(challenge => challenge.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(challenge => challenge.ConsumedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.HasIndex(challenge => challenge.ExpiresAtUtc);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(challenge => challenge.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAgents(ModelBuilder builder)
    {
        builder.Entity<AgentEnrollmentProfile>(entity =>
        {
            entity.ToTable("AgentEnrollmentProfiles", table =>
            {
                table.HasCheckConstraint(
                    "CK_AgentEnrollmentProfiles_EnrollmentCount",
                    "\"EnrolledCount\" >= 0 AND \"MaxEnrollments\" >= 1 AND " +
                    "\"MaxEnrollments\" <= 5000 AND " +
                    "\"EnrolledCount\" <= \"MaxEnrollments\"");
                table.HasCheckConstraint(
                    "CK_AgentEnrollmentProfiles_Expiry",
                    "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            });
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Name)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(profile => profile.TokenHash)
                .HasMaxLength(44)
                .IsFixedLength()
                .IsRequired();
            entity.HasIndex(profile => profile.TokenHash)
                .IsUnique()
                .HasDatabaseName("UX_AgentEnrollmentProfiles_TokenHash");
            entity.Property(profile => profile.AllowedHostnameSuffix)
                .HasMaxLength(150);
            entity.Property(profile => profile.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(profile => profile.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(profile => profile.RevokedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(profile => profile.Version).IsConcurrencyToken();
            entity.HasIndex(profile => profile.ExpiresAtUtc);
            entity.HasIndex(profile => profile.RevokedAtUtc);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(profile => profile.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ManagedAgent>(entity =>
        {
            entity.ToTable("ManagedAgents", table =>
            {
                table.HasCheckConstraint(
                    "CK_ManagedAgents_LastSequence",
                    "\"LastSequence\" >= 0");
                table.HasCheckConstraint(
                    "CK_ManagedAgents_Revocation",
                    "(\"RevokedAtUtc\" IS NULL AND \"RevokedReason\" IS NULL) OR " +
                    "(\"RevokedAtUtc\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ManagedAgents_KeyReplacementCount",
                    "\"KeyReplacementCount\" >= 0");
                table.HasCheckConstraint(
                    "CK_ManagedAgents_CheckInCapacity",
                    "\"CheckInBurstCount\" >= 0 AND " +
                    "\"CheckInQuotaCount\" >= 0");
            });
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.DeviceId)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(agent => agent.DeviceIdNormalized)
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(agent => agent.DeviceIdNormalized)
                .IsUnique()
                .HasDatabaseName("UX_ManagedAgents_DeviceIdNormalized");
            entity.Property(agent => agent.PublicKeyPem)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(agent => agent.KeyThumbprintSha256)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.HasIndex(agent => agent.KeyThumbprintSha256)
                .IsUnique()
                .HasDatabaseName("UX_ManagedAgents_KeyThumbprint");
            entity.Property(agent => agent.KeyProtection)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(agent => agent.Hostname)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(agent => agent.HostnameNormalized)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(agent => agent.SerialNumber).HasMaxLength(200);
            entity.Property(agent => agent.SerialNumberNormalized)
                .HasMaxLength(200);
            entity.Property(agent => agent.BiosUuid).HasMaxLength(100);
            entity.Property(agent => agent.BiosUuidNormalized)
                .HasMaxLength(100);
            entity.Property(agent => agent.Manufacturer).HasMaxLength(150);
            entity.Property(agent => agent.Model).HasMaxLength(150);
            entity.Property(agent => agent.MatchState)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(agent => agent.MatchReason).HasMaxLength(500);
            entity.Property(agent => agent.EnrolledAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(agent => agent.LastCheckInAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.LastCollectedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.LastAgentVersion).HasMaxLength(50);
            entity.Property(agent => agent.LastSeenIpAddress).HasMaxLength(100);
            entity.Property(agent => agent.RevokedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.RevokedReason).HasMaxLength(500);
            entity.Property(agent => agent.KeyReplacedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.CheckInBurstWindowStartedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.CheckInQuotaWindowStartedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(agent => agent.Version).IsConcurrencyToken();
            entity.HasIndex(agent => agent.HostnameNormalized);
            entity.HasIndex(agent => agent.LastCheckInAtUtc);
            entity.HasIndex(agent => agent.LinkedAssetId)
                .IsUnique()
                .HasFilter("\"LinkedAssetId\" IS NOT NULL")
                .HasDatabaseName("UX_ManagedAgents_LinkedAssetId");
            entity.HasOne<AgentEnrollmentProfile>()
                .WithMany()
                .HasForeignKey(agent => agent.EnrollmentProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(agent => agent.LinkedAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AgentKeyRecoveryAuthorization>(entity =>
        {
            entity.ToTable("AgentKeyRecoveryAuthorizations", table =>
            {
                table.HasCheckConstraint(
                    "CK_AgentKeyRecoveryAuthorizations_Expiry",
                    "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                table.HasCheckConstraint(
                    "CK_AgentKeyRecoveryAuthorizations_Consumption",
                    "(\"ConsumedAtUtc\" IS NULL AND " +
                    "\"ConsumedKeyThumbprintSha256\" IS NULL) OR " +
                    "(\"ConsumedAtUtc\" IS NOT NULL AND " +
                    "\"ConsumedKeyThumbprintSha256\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AgentKeyRecoveryAuthorizations_Revocation",
                    "(\"RevokedAtUtc\" IS NULL AND " +
                    "\"RevokedByUserId\" IS NULL AND \"RevokedReason\" IS NULL) OR " +
                    "(\"RevokedAtUtc\" IS NOT NULL AND " +
                    "\"RevokedByUserId\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AgentKeyRecoveryAuthorizations_ClosedState",
                    "NOT (\"ConsumedAtUtc\" IS NOT NULL AND " +
                    "\"RevokedAtUtc\" IS NOT NULL)");
            });
            entity.HasKey(authorization => authorization.Id);
            entity.Property(authorization => authorization.TokenHash)
                .HasMaxLength(44)
                .IsFixedLength()
                .IsRequired();
            entity.HasIndex(authorization => authorization.TokenHash)
                .IsUnique()
                .HasDatabaseName(
                    "UX_AgentKeyRecoveryAuthorizations_TokenHash");
            entity.Property(authorization => authorization.Reason)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(authorization => authorization.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(authorization => authorization.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(authorization => authorization.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(authorization => authorization.ConsumedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(
                    authorization =>
                        authorization.ConsumedKeyThumbprintSha256)
                .HasMaxLength(64)
                .IsFixedLength();
            entity.Property(authorization => authorization.RevokedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(authorization => authorization.RevokedReason)
                .HasMaxLength(500);
            entity.Property(authorization => authorization.Version)
                .IsConcurrencyToken();
            entity.HasIndex(authorization => authorization.AgentId)
                .IsUnique()
                .HasFilter(
                    "\"ConsumedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL")
                .HasDatabaseName(
                    "UX_AgentKeyRecoveryAuthorizations_PendingAgent");
            entity.HasIndex(authorization => authorization.ExpiresAtUtc);
            entity.HasOne<ManagedAgent>()
                .WithMany()
                .HasForeignKey(authorization => authorization.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(authorization => authorization.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(authorization => authorization.RevokedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ManagedAgentSnapshot>(entity =>
        {
            entity.ToTable("ManagedAgentSnapshots", table =>
            {
                table.HasCheckConstraint(
                    "CK_ManagedAgentSnapshots_Sequence",
                    "\"Sequence\" >= 1");
            });
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.BodySha256)
                .HasMaxLength(44)
                .IsFixedLength()
                .IsRequired();
            entity.Property(snapshot => snapshot.ContentSha256)
                .HasMaxLength(44)
                .IsFixedLength()
                .IsRequired();
            entity.Property(snapshot => snapshot.Hostname)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(snapshot => snapshot.SerialNumber).HasMaxLength(200);
            entity.Property(snapshot => snapshot.BiosUuid).HasMaxLength(100);
            entity.Property(snapshot => snapshot.AgentVersion)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(snapshot => snapshot.CollectedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(snapshot => snapshot.ReceivedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.HasIndex(snapshot => new
            {
                snapshot.AgentId,
                snapshot.SnapshotId,
            })
                .IsUnique()
                .HasDatabaseName("UX_ManagedAgentSnapshots_AgentSnapshot");
            entity.HasIndex(snapshot => new
            {
                snapshot.AgentId,
                snapshot.Sequence,
            })
                .IsUnique()
                .HasDatabaseName("UX_ManagedAgentSnapshots_AgentSequence");
            entity.HasIndex(snapshot => new
            {
                snapshot.AgentId,
                snapshot.ReceivedAtUtc,
            });
            entity.HasOne<ManagedAgent>()
                .WithMany()
                .HasForeignKey(snapshot => snapshot.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AgentRequestNonce>(entity =>
        {
            entity.ToTable("AgentRequestNonces", table =>
            {
                table.HasCheckConstraint(
                    "CK_AgentRequestNonces_Scope",
                    "\"Scope\" IN ('Enrollment', 'KeyRecovery', 'CheckIn')");
                table.HasCheckConstraint(
                    "CK_AgentRequestNonces_Expiry",
                    "\"ExpiresAtUtc\" > \"ReceivedAtUtc\"");
            });
            entity.HasKey(nonce => nonce.Id);
            entity.Property(nonce => nonce.Scope)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(nonce => nonce.NonceHash)
                .HasMaxLength(44)
                .IsFixedLength()
                .IsRequired();
            entity.Property(nonce => nonce.ReceivedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(nonce => nonce.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.HasIndex(nonce => new
            {
                nonce.Scope,
                nonce.ContextId,
                nonce.NonceHash,
            })
                .IsUnique()
                .HasDatabaseName(
                    "UX_AgentRequestNonces_ScopeContextNonce");
            entity.HasIndex(nonce => nonce.ExpiresAtUtc);
        });
    }

    private static void ConfigureImports(ModelBuilder builder)
    {
        builder.Entity<AssetImportJob>(entity =>
        {
            entity.ToTable("AssetImportJobs", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetImportJobs_Counts",
                    "\"TotalRows\" >= 0 AND \"ValidRows\" >= 0 AND " +
                    "\"ErrorRows\" >= 0 AND \"CreateRows\" >= 0 AND " +
                    "\"UpdateRows\" >= 0 AND " +
                    "\"ValidRows\" + \"ErrorRows\" = \"TotalRows\" AND " +
                    "\"CreateRows\" + \"UpdateRows\" = \"ValidRows\"");
                table.HasCheckConstraint(
                    "CK_AssetImportJobs_CommitCounts",
                    "\"CommittedCreateRows\" >= 0 AND " +
                    "\"CommittedUpdateRows\" >= 0 AND " +
                    "\"CommittedUnchangedRows\" >= 0 AND " +
                    "((\"Status\" = 'Committed' AND " +
                    "\"CommittedCreateRows\" + \"CommittedUpdateRows\" + " +
                    "\"CommittedUnchangedRows\" = \"ValidRows\") OR " +
                    "(\"Status\" <> 'Committed' AND \"CommittedCreateRows\" = 0 AND " +
                    "\"CommittedUpdateRows\" = 0 AND " +
                    "\"CommittedUnchangedRows\" = 0))");
                table.HasCheckConstraint(
                    "CK_AssetImportJobs_CommitState",
                    "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL " +
                    "AND \"CommittedByUserId\" IS NOT NULL " +
                    "AND \"CommittedByDisplayName\" IS NOT NULL) OR " +
                    "(\"Status\" <> 'Committed' AND \"CommittedAtUtc\" IS NULL " +
                    "AND \"CommittedByUserId\" IS NULL " +
                    "AND \"CommittedByDisplayName\" IS NULL)");
            });
            entity.HasKey(job => job.Id);
            entity.Property(job => job.OriginalFileName)
                .HasMaxLength(260)
                .IsRequired();
            entity.Property(job => job.FileHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(job => job.SnapshotHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(job => job.Mode)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(job => job.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(job => job.ValidatedRowsJson).IsRequired();
            entity.Property(job => job.ErrorsJson).IsRequired();
            entity.Property(job => job.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(job => job.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(job => job.CommittedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(job => job.CommittedByDisplayName).HasMaxLength(200);
            entity.Property(job => job.Version).IsConcurrencyToken();
            entity.HasIndex(job => job.CreatedAtUtc);
            entity.HasIndex(job => new { job.Status, job.CreatedAtUtc });

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(job => job.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(job => job.CommittedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReferenceDataImportJob>(entity =>
        {
            entity.ToTable("ReferenceDataImportJobs", table =>
            {
                table.HasCheckConstraint(
                    "CK_ReferenceDataImportJobs_Counts",
                    "\"TotalRows\" >= 0 AND \"ValidRows\" >= 0 AND " +
                    "\"ErrorRows\" >= 0 AND \"CreateRows\" >= 0 AND " +
                    "\"UpdateRows\" >= 0 AND " +
                    "\"ValidRows\" + \"ErrorRows\" = \"TotalRows\" AND " +
                    "\"CreateRows\" + \"UpdateRows\" = \"ValidRows\"");
                table.HasCheckConstraint(
                    "CK_ReferenceDataImportJobs_CommitCounts",
                    "\"CommittedCreateRows\" >= 0 AND " +
                    "\"CommittedUpdateRows\" >= 0 AND " +
                    "\"CommittedUnchangedRows\" >= 0 AND " +
                    "((\"Status\" = 'Committed' AND " +
                    "\"CommittedCreateRows\" + \"CommittedUpdateRows\" + " +
                    "\"CommittedUnchangedRows\" = \"ValidRows\") OR " +
                    "(\"Status\" <> 'Committed' AND \"CommittedCreateRows\" = 0 AND " +
                    "\"CommittedUpdateRows\" = 0 AND " +
                    "\"CommittedUnchangedRows\" = 0))");
                table.HasCheckConstraint(
                    "CK_ReferenceDataImportJobs_CommitState",
                    "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL " +
                    "AND \"CommittedByUserId\" IS NOT NULL " +
                    "AND \"CommittedByDisplayName\" IS NOT NULL) OR " +
                    "(\"Status\" <> 'Committed' AND \"CommittedAtUtc\" IS NULL " +
                    "AND \"CommittedByUserId\" IS NULL " +
                    "AND \"CommittedByDisplayName\" IS NULL)");
            });
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Resource)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(job => job.OriginalFileName)
                .HasMaxLength(260)
                .IsRequired();
            entity.Property(job => job.FileHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(job => job.SnapshotHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(job => job.Mode)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(job => job.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(job => job.ValidatedRowsJson).IsRequired();
            entity.Property(job => job.ErrorsJson).IsRequired();
            entity.Property(job => job.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(job => job.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(job => job.CommittedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(job => job.CommittedByDisplayName).HasMaxLength(200);
            entity.Property(job => job.Version).IsConcurrencyToken();
            entity.HasIndex(job => job.CreatedAtUtc);
            entity.HasIndex(job => new
            {
                job.Resource,
                job.Status,
                job.CreatedAtUtc,
            });

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(job => job.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(job => job.CommittedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureImportPreviewBudget(ModelBuilder builder)
    {
        builder.Entity<ImportPreviewReservation>(entity =>
        {
            entity.ToTable("ImportPreviewReservations", table =>
            {
                table.HasCheckConstraint(
                    "CK_ImportPreviewReservations_Bytes",
                    "\"PayloadBytes\" >= 0");
                table.HasCheckConstraint(
                    "CK_ImportPreviewReservations_Expiry",
                    "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            });
            entity.HasKey(reservation => new
            {
                reservation.Kind,
                reservation.PayloadId,
            });
            entity.Property(reservation => reservation.Kind)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(reservation => reservation.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(reservation => reservation.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(reservation => reservation.PurgedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(reservation => reservation.PreservedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.HasIndex(reservation => new
            {
                reservation.ExpiresAtUtc,
                reservation.PurgedAtUtc,
                reservation.PreservedAtUtc,
            });
            entity.HasIndex(reservation => new
            {
                reservation.CreatedByUserId,
                reservation.ExpiresAtUtc,
            });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(reservation => reservation.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ImportPreviewBudgetGate>(entity =>
        {
            entity.ToTable("ImportPreviewBudgetGates");
            entity.HasKey(gate => gate.Id);
            entity.HasData(new ImportPreviewBudgetGate { Id = 1, Revision = 0 });
        });
    }

    private static void ConfigureLegacyReconciliation(
        ModelBuilder builder)
    {
        builder.Entity<LegacyReconciliationRun>(entity =>
        {
            entity.ToTable(
                "LegacyReconciliationRuns",
                table => table.HasCheckConstraint(
                    "CK_LegacyReconciliationRuns_TargetAssetCount",
                    "\"TargetAssetCount\" >= 0"));
            entity.HasKey(run => run.Id);
            entity.Property(run => run.ReportFingerprint)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(run => run.ReportJson).IsRequired();
            entity.Property(run => run.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(run => run.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.HasIndex(run => run.AssetImportJobId)
                .IsUnique();
            entity.HasIndex(run => run.ReportFingerprint);
            entity.HasOne<AssetImportJob>()
                .WithMany()
                .HasForeignKey(run => run.AssetImportJobId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(run => run.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LegacyReconciliationSignOff>(entity =>
        {
            entity.ToTable("LegacyReconciliationSignOffs");
            entity.HasKey(signOff => signOff.Id);
            entity.Property(signOff => signOff.Decision)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(signOff => signOff.ReportFingerprint)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(signOff => signOff.SignedExceptionsJson)
                .IsRequired();
            entity.Property(signOff => signOff.Notes)
                .HasMaxLength(4000)
                .IsRequired();
            entity.Property(signOff => signOff.SignedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(signOff => signOff.SignedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.HasIndex(signOff => signOff.ReconciliationRunId)
                .IsUnique();
            entity.HasOne<LegacyReconciliationRun>()
                .WithMany()
                .HasForeignKey(signOff => signOff.ReconciliationRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(signOff => signOff.SignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureQrAndWarranties(
        ModelBuilder builder,
        bool isPostgres)
    {
        builder.Entity<QrIdentifier>(entity =>
        {
            entity.ToTable("QrIdentifiers", table =>
            {
                table.HasCheckConstraint(
                    "CK_QrIdentifiers_TokenVersion",
                    "\"TokenVersion\" > 0");
                table.HasCheckConstraint(
                    "CK_QrIdentifiers_RevocationFields",
                    "(\"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL) OR " +
                    "(\"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL)");
            });
            entity.HasKey(identifier => identifier.Id);
            entity.Ignore(identifier => identifier.IsActive);
            entity.Property(identifier => identifier.TokenHash)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(identifier => identifier.ProtectedToken)
                .HasMaxLength(2048)
                .IsRequired();
            entity.Property(identifier => identifier.IssuedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(identifier => identifier.IssuedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(identifier => identifier.RevokedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(identifier => identifier.RevokedByDisplayName)
                .HasMaxLength(200);
            entity.Property(identifier => identifier.RevocationReason)
                .HasMaxLength(500);
            entity.Property(identifier => identifier.Version).IsConcurrencyToken();

            entity.HasIndex(identifier => identifier.TokenHash)
                .IsUnique()
                .HasDatabaseName("UX_QrIdentifiers_TokenHash");
            entity.HasIndex(identifier => new
            {
                identifier.AssetId,
                identifier.TokenVersion,
            })
                .IsUnique()
                .HasDatabaseName("UX_QrIdentifiers_AssetVersion");
            entity.HasIndex(identifier => identifier.AssetId)
                .IsUnique()
                .HasFilter("\"RevokedAtUtc\" IS NULL")
                .HasDatabaseName("UX_QrIdentifiers_ActiveAsset");

            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(identifier => identifier.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(identifier => identifier.IssuedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(identifier => identifier.RevokedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WarrantyCoverage>(entity =>
        {
            entity.ToTable("WarrantyCoverages", table =>
            {
                table.HasCheckConstraint(
                    "CK_WarrantyCoverages_DateRange",
                    "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR " +
                    "\"EndDate\" >= \"StartDate\"");
                table.HasCheckConstraint(
                    "CK_WarrantyCoverages_ArchivedPrimary",
                    isPostgres
                        ? "\"ArchivedAtUtc\" IS NULL OR NOT \"IsPrimary\""
                        : "\"ArchivedAtUtc\" IS NULL OR \"IsPrimary\" = 0");
            });
            entity.HasKey(warranty => warranty.Id);
            entity.Ignore(warranty => warranty.IsArchived);
            entity.Property(warranty => warranty.Provider)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(warranty => warranty.CoverageType)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(warranty => warranty.Reference).HasMaxLength(200);
            entity.Property(warranty => warranty.Notes).HasMaxLength(2000);
            entity.Property(warranty => warranty.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(warranty => warranty.UpdatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(warranty => warranty.ArchivedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(warranty => warranty.Version).IsConcurrencyToken();

            entity.HasIndex(warranty => new
            {
                warranty.AssetId,
                warranty.EndDate,
            });
            entity.HasIndex(warranty => warranty.AssetId)
                .IsUnique()
                .HasFilter(
                    isPostgres
                        ? "\"IsPrimary\" AND \"ArchivedAtUtc\" IS NULL"
                        : "\"IsPrimary\" = 1 AND \"ArchivedAtUtc\" IS NULL")
                .HasDatabaseName("UX_WarrantyCoverages_ActivePrimary");

            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(warranty => warranty.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureIdentity(
        ModelBuilder builder,
        bool isPostgres)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable(
                "Users",
                table => table.HasCheckConstraint(
                    "CK_Users_ArchivedDisabled",
                    isPostgres
                        ? "\"ArchivedAtUtc\" IS NULL OR NOT \"IsEnabled\""
                        : "\"ArchivedAtUtc\" IS NULL OR \"IsEnabled\" = 0"));
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter)
                .IsRequired();
            entity.Property(user => user.ArchivedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(user => user.Version).IsConcurrencyToken();
            entity.HasIndex(user => user.IsEnabled);
            entity.HasIndex(user => user.ArchivedAtUtc);
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
    }

    private static void ConfigurePeopleAndAssignments(ModelBuilder builder)
    {
        builder.Entity<Person>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(person => person.Id);
            entity.Property(person => person.ExternalId).HasMaxLength(100);
            entity.Property(person => person.ExternalIdNormalized).HasMaxLength(100);
            entity.Property(person => person.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(person => person.Type)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(person => person.Email).HasMaxLength(320);
            entity.Property(person => person.Version).IsConcurrencyToken();
            entity.HasIndex(person => person.ExternalIdNormalized)
                .IsUnique()
                .HasFilter("\"ExternalIdNormalized\" IS NOT NULL")
                .HasDatabaseName("UX_People_ExternalIdNormalized");
            entity.HasIndex(person => new { person.IsActive, person.DisplayName });
        });

        builder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(department => department.Id);
            entity.Property(department => department.Code).HasMaxLength(50).IsRequired();
            entity.Property(department => department.CodeNormalized)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(department => department.Name).HasMaxLength(150).IsRequired();
            entity.Property(department => department.Version).IsConcurrencyToken();
            entity.HasIndex(department => department.CodeNormalized)
                .IsUnique()
                .HasDatabaseName("UX_Departments_CodeNormalized");
            entity.HasIndex(department => new { department.IsActive, department.Name });
        });

        builder.Entity<AssetAssignment>(entity =>
        {
            entity.ToTable("AssetAssignments", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetAssignments_ExactlyOneTarget",
                    "(\"PersonId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR " +
                    "(\"PersonId\" IS NULL AND \"DepartmentId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AssetAssignments_ReturnedFields",
                    "(\"ReturnedAtUtc\" IS NULL AND \"ReturnedByUserId\" IS NULL) OR " +
                    "(\"ReturnedAtUtc\" IS NOT NULL AND \"ReturnedByUserId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AssetAssignments_ExpectedReturnOn",
                    "\"ExpectedReturnOn\" IS NULL OR \"ExpectedReturnOn\" > '0001-01-01'");
            });
            entity.HasKey(assignment => assignment.Id);
            entity.Ignore(assignment => assignment.TargetType);
            entity.Ignore(assignment => assignment.IsOpen);
            entity.Property(assignment => assignment.TargetDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(assignment => assignment.TargetReference).HasMaxLength(200);
            entity.Property(assignment => assignment.AssignedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(assignment => assignment.AssignedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(assignment => assignment.AssignedCondition)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(assignment => assignment.AssignmentNotes).HasMaxLength(2000);
            entity.Property(assignment => assignment.ReturnedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(assignment => assignment.ReturnedByDisplayName).HasMaxLength(200);
            entity.Property(assignment => assignment.ReturnCondition)
                .HasConversion<string>()
                .HasMaxLength(30);
            entity.Property(assignment => assignment.ReturnNotes).HasMaxLength(2000);
            entity.Property(assignment => assignment.Version).IsConcurrencyToken();

            entity.HasIndex(assignment => assignment.AssetId)
                .IsUnique()
                .HasFilter("\"ReturnedAtUtc\" IS NULL")
                .HasDatabaseName("UX_AssetAssignments_OpenAsset");
            entity.HasIndex(
                assignment => new { assignment.PersonId, assignment.ReturnedAtUtc });
            entity.HasIndex(
                assignment => new { assignment.DepartmentId, assignment.ReturnedAtUtc });
            entity.HasIndex(assignment => new { assignment.AssetId, assignment.AssignedAtUtc });

            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(assignment => assignment.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(assignment => assignment.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>()
                .WithMany()
                .HasForeignKey(assignment => assignment.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(assignment => assignment.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(assignment => assignment.ReturnedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(assignment => assignment.AssignedLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(assignment => assignment.ReturnLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAssets(ModelBuilder builder, bool isPostgres)
    {
        builder.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets");
            entity.HasKey(asset => asset.Id);

            entity.Property(asset => asset.AssetTag).HasMaxLength(100).IsRequired();
            entity.Property(asset => asset.AssetTagNormalized).HasMaxLength(100).IsRequired();
            entity.HasIndex(asset => asset.AssetTagNormalized)
                .IsUnique()
                .HasDatabaseName("UX_Assets_AssetTagNormalized");

            entity.Property(asset => asset.DeviceName).HasMaxLength(150);
            entity.Property(asset => asset.Description).HasMaxLength(2000);
            entity.Property(asset => asset.Manufacturer).HasMaxLength(150);
            entity.Property(asset => asset.Model).HasMaxLength(150);
            entity.Property(asset => asset.SerialNumber).HasMaxLength(200);
            entity.Property(asset => asset.SerialNumberNormalized).HasMaxLength(200);
            entity.HasIndex(asset => asset.SerialNumberNormalized)
                .IsUnique()
                .HasFilter("\"SerialNumberNormalized\" IS NOT NULL")
                .HasDatabaseName("UX_Assets_SerialNumberNormalized");
            entity.Property(asset => asset.LanMacAddress).HasMaxLength(17);
            entity.Property(asset => asset.Ownership).HasMaxLength(100);

            entity.Property(asset => asset.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(asset => asset.Condition)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(asset => asset.PurchaseCost)
                .HasConversion(PurchaseCostMinorUnitsConverter)
                .HasColumnType(isPostgres ? "bigint" : "INTEGER");
            entity.Property(asset => asset.Currency).HasMaxLength(3);
            entity.Property(asset => asset.Supplier).HasMaxLength(200);
            entity.Property(asset => asset.OperatingSystem).HasMaxLength(100);
            entity.Property(asset => asset.OperatingSystemVersion).HasMaxLength(100);
            entity.Property(asset => asset.Notes).HasMaxLength(4000);
            entity.Property(asset => asset.LegacyId).HasMaxLength(200);
            entity.Property(asset => asset.RetirementReason).HasMaxLength(1000);
            entity.Property(asset => asset.DisposalReason).HasMaxLength(1000);
            entity.Property(asset => asset.CreatedAtUtc).HasConversion(UtcTimestampConverter);
            entity.Property(asset => asset.UpdatedAtUtc).HasConversion(UtcTimestampConverter);
            entity.Property(asset => asset.RetiredAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(asset => asset.DisposedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(asset => asset.Version).IsConcurrencyToken();

            entity.HasIndex(asset => asset.Status);
            entity.HasIndex(asset => asset.CategoryId);
            entity.HasIndex(asset => asset.LocationId);
            entity.HasIndex(asset => asset.LegacyId);

            entity.HasOne<AssetCategory>()
                .WithMany()
                .HasForeignKey(asset => asset.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(asset => asset.LocationId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureReferenceData(ModelBuilder builder)
    {
        builder.Entity<AssetCategory>(entity =>
        {
            entity.ToTable("AssetCategories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Code).HasMaxLength(50).IsRequired();
            entity.Property(category => category.CodeNormalized).HasMaxLength(50).IsRequired();
            entity.Property(category => category.Name).HasMaxLength(150).IsRequired();
            entity.Property(category => category.Description).HasMaxLength(1000);
            entity.Property(category => category.Version).IsConcurrencyToken();
            entity.HasIndex(category => category.CodeNormalized)
                .IsUnique()
                .HasDatabaseName("UX_AssetCategories_CodeNormalized");
        });

        builder.Entity<Location>(entity =>
        {
            entity.ToTable("Locations", table =>
            {
                table.HasCheckConstraint(
                    "CK_Locations_NotOwnParent",
                    "\"ParentLocationId\" IS NULL OR \"ParentLocationId\" <> \"Id\"");
            });
            entity.HasKey(location => location.Id);
            entity.Property(location => location.Code).HasMaxLength(50).IsRequired();
            entity.Property(location => location.CodeNormalized).HasMaxLength(50).IsRequired();
            entity.Property(location => location.Name).HasMaxLength(150).IsRequired();
            entity.Property(location => location.Type).HasMaxLength(80);
            entity.Property(location => location.Version).IsConcurrencyToken();
            entity.HasIndex(location => location.CodeNormalized)
                .IsUnique()
                .HasDatabaseName("UX_Locations_CodeNormalized");
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(location => location.ParentLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureHistoryAndAudit(ModelBuilder builder)
    {
        builder.Entity<AssetEvent>(entity =>
        {
            entity.ToTable("AssetEvents");
            entity.HasKey(assetEvent => assetEvent.Id);
            entity.Property(assetEvent => assetEvent.Type)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(assetEvent => assetEvent.Source)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(assetEvent => assetEvent.ActorDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(assetEvent => assetEvent.Summary).HasMaxLength(500).IsRequired();
            entity.Property(assetEvent => assetEvent.DetailsJson).IsRequired();
            entity.Property(assetEvent => assetEvent.CorrelationId).HasMaxLength(100);
            entity.Property(assetEvent => assetEvent.OccurredAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.HasIndex(assetEvent => new { assetEvent.AssetId, assetEvent.OccurredAtUtc });
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(assetEvent => assetEvent.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(assetEvent => assetEvent.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.ActorDisplayName).HasMaxLength(200);
            entity.Property(audit => audit.Action).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.TargetId).HasMaxLength(200);
            entity.Property(audit => audit.Outcome).HasMaxLength(50).IsRequired();
            entity.Property(audit => audit.DetailsJson);
            entity.Property(audit => audit.IpAddress).HasMaxLength(100);
            entity.Property(audit => audit.Client).HasMaxLength(500);
            entity.Property(audit => audit.CorrelationId).HasMaxLength(100);
            entity.Property(audit => audit.OccurredAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.HasIndex(audit => audit.OccurredAtUtc);
            entity.HasIndex(audit => new { audit.Action, audit.OccurredAtUtc });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(audit => audit.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSchoolProfile(ModelBuilder builder)
    {
        builder.Entity<SchoolProfile>(entity =>
        {
            entity.ToTable("SchoolProfile");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
            entity.Property(profile => profile.Code).HasMaxLength(50).IsRequired();
            entity.Property(profile => profile.TimeZone).HasMaxLength(100).IsRequired();
            entity.Property(profile => profile.QrBaseUrl).HasMaxLength(500).IsRequired();
            entity.Property(profile => profile.Version).IsConcurrencyToken();
        });
    }

    private void EnforceAppendOnlyRecords()
    {
        var invalidEvent = ChangeTracker.Entries<AssetEvent>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (invalidEvent is not null)
        {
            throw new InvalidOperationException("Asset events are append-only.");
        }

        var invalidAudit = ChangeTracker.Entries<AuditLog>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (invalidAudit is not null)
        {
            throw new InvalidOperationException("Audit logs are append-only.");
        }

        var deletedAssignment = ChangeTracker.Entries<AssetAssignment>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedAssignment is not null)
        {
            throw new InvalidOperationException("Asset assignments are retained, not deleted.");
        }

        var deletedPerson = ChangeTracker.Entries<Person>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedPerson is not null)
        {
            throw new InvalidOperationException("People are archived, not deleted.");
        }

        var deletedDepartment = ChangeTracker.Entries<Department>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedDepartment is not null)
        {
            throw new InvalidOperationException("Departments are archived, not deleted.");
        }

        var deletedQrIdentifier = ChangeTracker.Entries<QrIdentifier>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedQrIdentifier is not null)
        {
            throw new InvalidOperationException(
                "QR identifiers are revoked, not deleted.");
        }

        var deletedWarranty = ChangeTracker.Entries<WarrantyCoverage>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedWarranty is not null)
        {
            throw new InvalidOperationException(
                "Warranty coverage is archived, not deleted.");
        }

        var deletedImportJob = ChangeTracker.Entries<AssetImportJob>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedImportJob is not null)
        {
            throw new InvalidOperationException(
                "Asset import jobs are retained, not deleted.");
        }

        var deletedReferenceImportJob = ChangeTracker.Entries<ReferenceDataImportJob>()
            .FirstOrDefault(entry => entry.State == EntityState.Deleted);
        if (deletedReferenceImportJob is not null)
        {
            throw new InvalidOperationException(
                "Reference-data import jobs are retained, not deleted.");
        }

        var changedReconciliationRun =
            ChangeTracker.Entries<LegacyReconciliationRun>()
                .FirstOrDefault(
                    entry =>
                        entry.State is EntityState.Modified or
                            EntityState.Deleted);
        if (changedReconciliationRun is not null)
        {
            throw new InvalidOperationException(
                "Legacy reconciliation runs are append-only.");
        }

        var changedReconciliationSignOff =
            ChangeTracker.Entries<LegacyReconciliationSignOff>()
                .FirstOrDefault(
                    entry =>
                        entry.State is EntityState.Modified or
                            EntityState.Deleted);
        if (changedReconciliationSignOff is not null)
        {
            throw new InvalidOperationException(
                "Legacy reconciliation sign-offs are append-only.");
        }

        var changedAgentSnapshot = ChangeTracker
            .Entries<ManagedAgentSnapshot>()
            .FirstOrDefault(
                entry =>
                    entry.State is EntityState.Modified or EntityState.Deleted);
        if (changedAgentSnapshot is not null)
        {
            throw new InvalidOperationException(
                "Managed-agent snapshots are append-only.");
        }

        if (ChangeTracker.Entries<ManagedAgent>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Managed agents are revoked, not deleted.");
        }

        if (ChangeTracker.Entries<AgentEnrollmentProfile>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Agent enrollment profiles are revoked, not deleted.");
        }

        if (ChangeTracker.Entries<AgentKeyRecoveryAuthorization>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Agent key-recovery authorizations are retained, not deleted.");
        }

        if (ChangeTracker.Entries<StocktakeSession>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Stocktake sessions are retained, not deleted.");
        }

        if (ChangeTracker.Entries<StocktakeExpectedAsset>()
            .Any(
                entry =>
                    entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Stocktake expected snapshots are append-only.");
        }

        if (ChangeTracker.Entries<StocktakeObservation>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Stocktake observations are retained, not deleted.");
        }

        if (ChangeTracker.Entries<AssetBulkChangeJob>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Bulk-change previews are retained, not deleted.");
        }

        if (ChangeTracker.Entries<AssetTagPolicy>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "The asset-tag policy cannot be deleted.");
        }

        if (ChangeTracker.Entries<AssetBundle>()
            .Any(entry => entry.State == EntityState.Deleted) ||
            ChangeTracker.Entries<AssetBundleMember>()
                .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Asset bundles and their membership history are archived, not deleted.");
        }

        if (ChangeTracker.Entries<LeaverCase>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Leaver cases are retained, not deleted.");
        }

        if (ChangeTracker.Entries<AssetAttachment>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Asset attachments are archived, not deleted.");
        }

        var changedAttachmentContent = ChangeTracker
            .Entries<AssetAttachment>()
            .Where(entry => entry.State == EntityState.Modified)
            .SelectMany(entry => entry.Properties)
            .FirstOrDefault(
                property =>
                    property.IsModified &&
                    property.Metadata.Name is
                        nameof(AssetAttachment.AssetId) or
                        nameof(AssetAttachment.Category) or
                        nameof(AssetAttachment.FileName) or
                        nameof(AssetAttachment.ContentType) or
                        nameof(AssetAttachment.Content) or
                        nameof(AssetAttachment.SizeBytes) or
                        nameof(AssetAttachment.Sha256) or
                        nameof(AssetAttachment.CreatedAtUtc) or
                        nameof(AssetAttachment.CreatedByUserId) or
                        nameof(AssetAttachment.CreatedByDisplayName));
        if (changedAttachmentContent is not null)
        {
            throw new InvalidOperationException(
                "Asset attachment evidence is immutable after upload.");
        }

        if (ChangeTracker.Entries<SavedAssetSearch>()
            .Any(entry => entry.State == EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Saved searches are archived, not deleted.");
        }
    }
}
