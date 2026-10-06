using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.People;
using Inventory.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inventory.Infrastructure.Data;

public static class Milestone7CustodyModelConfiguration
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcTimestampConverter =
        new(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?>
        NullableUtcTimestampConverter =
            new(
                value => value.HasValue ? value.Value.UtcDateTime : null,
                value => value.HasValue
                    ? new DateTimeOffset(
                        DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                    : null);

    public static void Configure(ModelBuilder builder, bool isPostgres)
    {
        ConfigureExpectedReturn(builder);
        ConfigureLeaverCases(builder);
        ConfigureAttachments(builder, isPostgres);
        ConfigureSavedSearches(builder, isPostgres);
    }

    private static void ConfigureExpectedReturn(ModelBuilder builder)
    {
        builder.Entity<AssetAssignment>()
            .HasIndex(
                assignment => new
                {
                    assignment.ExpectedReturnOn,
                    assignment.ReturnedAtUtc,
                })
            .HasDatabaseName("IX_AssetAssignments_ExpectedReturnOn_ReturnedAtUtc");
    }

    private static void ConfigureLeaverCases(ModelBuilder builder)
    {
        builder.Entity<LeaverCase>(entity =>
        {
            entity.ToTable("LeaverCases", table =>
            {
                table.HasCheckConstraint(
                    "CK_LeaverCases_Lifecycle",
                    "(\"Status\" = 'Open' AND \"CompletedAtUtc\" IS NULL AND " +
                    "\"CompletedByUserId\" IS NULL AND \"CancelledAtUtc\" IS NULL AND " +
                    "\"CancelledByUserId\" IS NULL) OR " +
                    "(\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND " +
                    "\"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND " +
                    "\"CancelledByUserId\" IS NULL) OR " +
                    "(\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL AND " +
                    "\"CancelledByUserId\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND " +
                    "\"CompletedByUserId\" IS NULL) OR " +
                    "(\"Status\" = 'Rehired' AND \"CompletedAtUtc\" IS NOT NULL AND " +
                    "\"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND " +
                    "\"CancelledByUserId\" IS NULL)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Notes).HasMaxLength(2000);
            entity.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(item => item.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(item => item.CompletedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(item => item.CompletedByDisplayName).HasMaxLength(200);
            entity.Property(item => item.CancelledAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(item => item.CancelledByDisplayName).HasMaxLength(200);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.Status, item.LeavingOn });
            entity.HasIndex(item => item.PersonId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'")
                .HasDatabaseName("UX_LeaverCases_OpenPerson");
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(item => item.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.CancelledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAttachments(
        ModelBuilder builder,
        bool isPostgres)
    {
        builder.Entity<AssetAttachment>(entity =>
        {
            entity.ToTable("AssetAttachments", table =>
            {
                table.HasCheckConstraint(
                    "CK_AssetAttachments_ContentSize",
                    "\"SizeBytes\" >= 1 AND \"SizeBytes\" <= 10485760 AND " +
                    "length(\"Content\") = \"SizeBytes\"");
                table.HasCheckConstraint(
                    "CK_AssetAttachments_Archive",
                    "(\"ArchivedAtUtc\" IS NULL AND \"ArchivedByUserId\" IS NULL) OR " +
                    "(\"ArchivedAtUtc\" IS NOT NULL AND \"ArchivedByUserId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AssetAttachments_FileTypeAndHash",
                    "\"ContentType\" IN ('application/pdf', 'image/jpeg', 'image/png') " +
                    "AND length(\"Sha256\") = 64");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Category)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(item => item.FileName).HasMaxLength(255).IsRequired();
            entity.Property(item => item.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Content)
                .HasColumnType(isPostgres ? "bytea" : "BLOB")
                .IsRequired();
            entity.Property(item => item.Sha256)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(item => item.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(item => item.ArchivedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(item => item.ArchivedByDisplayName).HasMaxLength(200);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(
                item => new
                {
                    item.AssetId,
                    item.ArchivedAtUtc,
                    item.CreatedAtUtc,
                });
            entity.HasIndex(item => new { item.AssetId, item.Sha256 });
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(item => item.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.ArchivedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSavedSearches(
        ModelBuilder builder,
        bool isPostgres)
    {
        builder.Entity<SavedAssetSearch>(entity =>
        {
            entity.ToTable("SavedAssetSearches", table =>
            {
                table.HasCheckConstraint(
                    "CK_SavedAssetSearches_SchemaVersion",
                    "\"SchemaVersion\" = 1");
                table.HasCheckConstraint(
                    "CK_SavedAssetSearches_ArchivedDefault",
                    isPostgres
                        ? "\"ArchivedAtUtc\" IS NULL OR NOT \"IsDefault\""
                        : "\"ArchivedAtUtc\" IS NULL OR \"IsDefault\" = 0");
                table.HasCheckConstraint(
                    "CK_SavedAssetSearches_FilterSize",
                    "length(\"FilterJson\") BETWEEN 2 AND 16000");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(100).IsRequired();
            entity.Property(item => item.NameNormalized)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(item => item.FilterJson).HasMaxLength(16000).IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(item => item.UpdatedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(item => item.ArchivedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.UserId, item.NameNormalized })
                .IsUnique()
                .HasFilter("\"ArchivedAtUtc\" IS NULL")
                .HasDatabaseName("UX_SavedAssetSearches_ActiveName");
            entity.HasIndex(item => item.UserId)
                .IsUnique()
                .HasFilter(
                    isPostgres
                        ? "\"IsDefault\" AND \"ArchivedAtUtc\" IS NULL"
                        : "\"IsDefault\" = 1 AND \"ArchivedAtUtc\" IS NULL")
                .HasDatabaseName("UX_SavedAssetSearches_DefaultUser");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
