using Inventory.Domain.Assets;
using Inventory.Domain.Stocktakes;
using Inventory.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inventory.Infrastructure.Data;

public static class StocktakeModelConfiguration
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
                    ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                    : null);

    /// <summary>
    /// Called by InventoryDbContext.OnModelCreating when the stocktake feature is integrated.
    /// </summary>
    public static void ConfigureStocktakes(this ModelBuilder builder)
    {
        builder.Entity<StocktakeSession>(entity =>
        {
            entity.ToTable("StocktakeSessions", table =>
            {
                table.HasCheckConstraint(
                    "CK_StocktakeSessions_Scope",
                    "(\"Scope\" = 'All' AND \"ScopeLocationId\" IS NULL AND " +
                    "\"ScopeCategoryId\" IS NULL) OR " +
                    "(\"Scope\" = 'Location' AND \"ScopeLocationId\" IS NOT NULL AND " +
                    "\"ScopeCategoryId\" IS NULL) OR " +
                    "(\"Scope\" = 'Category' AND \"ScopeLocationId\" IS NULL AND " +
                    "\"ScopeCategoryId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_StocktakeSessions_Lifecycle",
                    "(\"Status\" = 'InProgress' AND \"CompletedAtUtc\" IS NULL AND " +
                    "\"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND " +
                    "\"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND " +
                    "\"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR " +
                    "(\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND " +
                    "\"CompletedByUserId\" IS NOT NULL AND \"CompletedByDisplayName\" IS NOT NULL " +
                    "AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND " +
                    "\"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR " +
                    "(\"Status\" = 'Cancelled' AND \"CompletedAtUtc\" IS NULL AND " +
                    "\"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND " +
                    "\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND " +
                    "\"CancelledByDisplayName\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
            });
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Name).HasMaxLength(200).IsRequired();
            entity.Property(session => session.Scope)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(session => session.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(session => session.StartedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(session => session.StartedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(session => session.CompletedAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(session => session.CompletedByDisplayName).HasMaxLength(200);
            entity.Property(session => session.CancelledAtUtc)
                .HasConversion(NullableUtcTimestampConverter);
            entity.Property(session => session.CancelledByDisplayName).HasMaxLength(200);
            entity.Property(session => session.CancellationReason).HasMaxLength(1000);
            entity.Property(session => session.Version).IsConcurrencyToken();
            entity.HasIndex(session => new { session.Status, session.StartedAtUtc });
            entity.HasIndex(session => session.ScopeLocationId);
            entity.HasIndex(session => session.ScopeCategoryId);
            entity.HasOne<Inventory.Domain.Locations.Location>()
                .WithMany()
                .HasForeignKey(session => session.ScopeLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AssetCategory>()
                .WithMany()
                .HasForeignKey(session => session.ScopeCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(session => session.StartedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(session => session.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(session => session.CancelledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StocktakeExpectedAsset>(entity =>
        {
            entity.ToTable("StocktakeExpectedAssets");
            entity.HasKey(expected => expected.Id);
            entity.Property(expected => expected.AssetTag).HasMaxLength(100).IsRequired();
            entity.Property(expected => expected.DeviceName).HasMaxLength(150);
            entity.Property(expected => expected.CategoryCode).HasMaxLength(50).IsRequired();
            entity.Property(expected => expected.CategoryName).HasMaxLength(150).IsRequired();
            entity.Property(expected => expected.ExpectedStatus)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(expected => expected.ExpectedCondition)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(expected => expected.ExpectedLocationCode).HasMaxLength(50);
            entity.Property(expected => expected.ExpectedLocationName).HasMaxLength(150);
            entity.HasIndex(expected => new { expected.SessionId, expected.AssetId })
                .IsUnique()
                .HasDatabaseName("UX_StocktakeExpectedAssets_SessionAsset");
            entity.HasIndex(expected => new { expected.SessionId, expected.AssetTag });
            entity.HasOne<StocktakeSession>()
                .WithMany()
                .HasForeignKey(expected => expected.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(expected => expected.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StocktakeObservation>(entity =>
        {
            entity.ToTable("StocktakeObservations");
            entity.HasKey(observation => observation.Id);
            entity.Property(observation => observation.AssetTag).HasMaxLength(100).IsRequired();
            entity.Property(observation => observation.DeviceName).HasMaxLength(150);
            entity.Property(observation => observation.CategoryCode).HasMaxLength(50).IsRequired();
            entity.Property(observation => observation.CategoryName).HasMaxLength(150).IsRequired();
            entity.Property(observation => observation.ObservedLocationCode)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(observation => observation.ObservedLocationName)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(observation => observation.ObservedCondition)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(observation => observation.Note).HasMaxLength(1000);
            entity.Property(observation => observation.ObservedAtUtc)
                .HasConversion(UtcTimestampConverter);
            entity.Property(observation => observation.ObservedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.HasIndex(observation => new { observation.SessionId, observation.AssetId })
                .IsUnique()
                .HasDatabaseName("UX_StocktakeObservations_SessionAsset");
            entity.HasIndex(observation => new { observation.SessionId, observation.ObservedAtUtc });
            entity.HasOne<StocktakeSession>()
                .WithMany()
                .HasForeignKey(observation => observation.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(observation => observation.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Inventory.Domain.Locations.Location>()
                .WithMany()
                .HasForeignKey(observation => observation.ObservedLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(observation => observation.ObservedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
