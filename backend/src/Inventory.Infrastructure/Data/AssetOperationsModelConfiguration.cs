using Inventory.Domain.AssetTags;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Inventory.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inventory.Infrastructure.Data;

public static class AssetOperationsModelConfiguration
{
    private static readonly ValueConverter<DateTimeOffset, DateTime>
        UtcTimestampConverter =
            new(
                value => value.UtcDateTime,
                value => new DateTimeOffset(
                    DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?>
        NullableUtcTimestampConverter =
            new(
                value => value.HasValue ? value.Value.UtcDateTime : null,
                value => value.HasValue
                    ? new DateTimeOffset(
                        DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                    : null);

    public static void ConfigureAssetOperations(
        this ModelBuilder builder,
        bool isPostgres)
    {
        ConfigureAssetTagPolicy(builder);
        ConfigureBulkChanges(builder);
        ConfigureBundles(builder, isPostgres);
    }

    private static void ConfigureAssetTagPolicy(ModelBuilder builder)
    {
        builder.Entity<AssetTagPolicy>(entity =>
        {
            entity.ToTable("AssetTagPolicies", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetTagPolicies_Singleton",
                    "\"Id\" = 1");
                table.HasCheckConstraint(
                    "CK_AssetTagPolicies_Sequence",
                    "\"NumericWidth\" BETWEEN 2 AND 12 AND " +
                    "\"NextNumber\" >= 1 AND " +
                    "length(CAST(\"NextNumber\" - 1 AS TEXT)) <= \"NumericWidth\"");
            });
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.Id).ValueGeneratedNever();
            entity.Property(policy => policy.Prefix)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(policy => policy.Version).IsConcurrencyToken();
            entity.HasData(
                new
                {
                    Id = AssetTagPolicy.SingletonId,
                    Prefix = "AST-",
                    NumericWidth = 6,
                    NextNumber = 1L,
                    Version = Guid.Parse(
                        "00000000-0000-0000-0000-000000000701"),
                });
        });
    }

    private static void ConfigureBulkChanges(ModelBuilder builder)
    {
        builder.Entity<AssetBulkChangeJob>(entity =>
        {
            entity.ToTable("AssetBulkChangeJobs", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetBulkChangeJobs_AssetCount",
                    "\"AssetCount\" BETWEEN 1 AND 200");
                table.HasCheckConstraint(
                    "CK_AssetBulkChangeJobs_Expiry",
                    "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                table.HasCheckConstraint(
                    "CK_AssetBulkChangeJobs_Commit",
                    "(\"Status\" = 'Previewed' AND \"CommittedAtUtc\" IS NULL) OR " +
                    "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL)");
            });
            entity.HasKey(job => job.Id);
            entity.Property(job => job.OperationKind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(job => job.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(job => job.OperationJson)
                .HasMaxLength(8000)
                .IsRequired();
            entity.Property(job => job.ItemsJson)
                .HasMaxLength(32000)
                .IsRequired();
            entity.Property(job => job.Reason)
                .HasMaxLength(1000)
                .IsRequired();
            entity.Property(job => job.Fingerprint)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(job => job.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(job => job.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(job => job.ExpiresAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(job => job.CommittedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(job => job.Version).IsConcurrencyToken();
            entity.HasIndex(job => job.Fingerprint);
            entity.HasIndex(job => new { job.Status, job.ExpiresAtUtc });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(job => job.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureBundles(
        ModelBuilder builder,
        bool isPostgres)
    {
        builder.Entity<AssetBundle>(entity =>
        {
            entity.ToTable("AssetBundles");
            entity.HasKey(bundle => bundle.Id);
            entity.Property(bundle => bundle.Code)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(bundle => bundle.CodeNormalized)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(bundle => bundle.Name)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(bundle => bundle.NameNormalized)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(bundle => bundle.Description).HasMaxLength(1000);
            entity.Property(bundle => bundle.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(bundle => bundle.UpdatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(bundle => bundle.ArchivedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(bundle => bundle.Version).IsConcurrencyToken();
            entity.HasIndex(bundle => bundle.CodeNormalized)
                .IsUnique()
                .HasDatabaseName("UX_AssetBundles_CodeNormalized");
            entity.HasIndex(bundle => bundle.NameNormalized)
                .IsUnique()
                .HasDatabaseName("UX_AssetBundles_NameNormalized");
            entity.Ignore(bundle => bundle.CurrentMembers);
            entity.HasMany(bundle => bundle.Members)
                .WithOne()
                .HasForeignKey(member => member.BundleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(bundle => bundle.Members)
                .HasField("_members")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<AssetBundleMember>(entity =>
        {
            entity.ToTable("AssetBundleMembers", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetBundleMembers_OtherRole",
                    "(\"Role\" = 'Other' AND \"OtherRole\" IS NOT NULL) OR " +
                    "(\"Role\" <> 'Other' AND \"OtherRole\" IS NULL)");
                table.HasCheckConstraint(
                    "CK_AssetBundleMembers_Lifecycle",
                    isPostgres
                        ? "(\"RemovedAtUtc\" IS NULL) OR (NOT \"IsActive\")"
                        : "(\"RemovedAtUtc\" IS NULL) OR (\"IsActive\" = 0)");
            });
            entity.HasKey(member => member.Id);
            entity.Property(member => member.Role)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(member => member.OtherRole).HasMaxLength(100);
            entity.Property(member => member.AddedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(member => member.RemovedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.HasIndex(member => new { member.BundleId, member.AssetId })
                .IsUnique()
                .HasDatabaseName("UX_AssetBundleMembers_BundleAsset");
            entity.HasIndex(member => member.AssetId)
                .IsUnique()
                .HasFilter(isPostgres ? "\"IsActive\"" : "\"IsActive\" = 1")
                .HasDatabaseName("IX_AssetBundleMembers_AssetId_Active");
            entity.HasOne<Inventory.Domain.Assets.Asset>()
                .WithMany()
                .HasForeignKey(member => member.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
