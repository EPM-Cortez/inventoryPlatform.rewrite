using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreCustodyTriggersAfterMilestone7Rebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_SavedAssetSearches_LimitActivePerUser"
                BEFORE INSERT ON "SavedAssetSearches"
                WHEN NEW."ArchivedAtUtc" IS NULL
                  AND (
                      SELECT COUNT(*)
                      FROM "SavedAssetSearches"
                      WHERE "UserId" = NEW."UserId"
                        AND "ArchivedAtUtc" IS NULL
                  ) >= 50
                BEGIN
                    SELECT RAISE(ABORT, 'saved_search_limit_reached');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Assets_BlockTerminalStatusWithBundle"
                BEFORE UPDATE OF "Status" ON "Assets"
                WHEN NEW."Status" IN ('Retired', 'Disposed')
                  AND EXISTS (
                      SELECT 1
                      FROM "AssetBundleMembers"
                      WHERE "AssetId" = OLD."Id"
                        AND "IsActive" = 1)
                BEGIN
                    SELECT RAISE(ABORT, 'asset_has_active_bundle');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetBundleMembers_BlockTerminalAsset_Insert"
                BEFORE INSERT ON "AssetBundleMembers"
                WHEN NEW."IsActive" = 1
                  AND EXISTS (
                      SELECT 1
                      FROM "Assets"
                      WHERE "Id" = NEW."AssetId"
                        AND "Status" IN ('Retired', 'Disposed'))
                BEGIN
                    SELECT RAISE(ABORT, 'bundle_asset_terminal');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetBundleMembers_BlockTerminalAsset_Update"
                BEFORE UPDATE OF "AssetId", "IsActive" ON "AssetBundleMembers"
                WHEN NEW."IsActive" = 1
                  AND EXISTS (
                      SELECT 1
                      FROM "Assets"
                      WHERE "Id" = NEW."AssetId"
                        AND "Status" IN ('Retired', 'Disposed'))
                BEGIN
                    SELECT RAISE(ABORT, 'bundle_asset_terminal');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAttachments_LimitPerAsset"
                BEFORE INSERT ON "AssetAttachments"
                WHEN (
                    SELECT COUNT(*)
                    FROM "AssetAttachments"
                    WHERE "AssetId" = NEW."AssetId"
                ) >= 100
                  OR COALESCE((
                      SELECT SUM("SizeBytes")
                      FROM "AssetAttachments"
                      WHERE "AssetId" = NEW."AssetId"
                  ), 0) + NEW."SizeBytes" > 262144000
                BEGIN
                    SELECT RAISE(ABORT, 'attachment_quota_exceeded');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetBulkChangeJobs_LimitActivePerUser"
                BEFORE INSERT ON "AssetBulkChangeJobs"
                WHEN NEW."Status" = 'Previewed'
                  AND julianday(NEW."ExpiresAtUtc") > julianday(CURRENT_TIMESTAMP)
                  AND (
                      SELECT COUNT(*)
                      FROM "AssetBulkChangeJobs"
                      WHERE "CreatedByUserId" = NEW."CreatedByUserId"
                        AND "Status" = 'Previewed'
                        AND julianday("ExpiresAtUtc") > julianday(CURRENT_TIMESTAMP)
                  ) >= 20
                BEGIN
                    SELECT RAISE(ABORT, 'bulk_preview_limit_reached');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_StocktakeSessions_LimitInProgress"
                BEFORE INSERT ON "StocktakeSessions"
                WHEN NEW."Status" = 'InProgress'
                  AND (
                      SELECT COUNT(*)
                      FROM "StocktakeSessions"
                      WHERE "Status" = 'InProgress'
                  ) >= 10
                BEGIN
                    SELECT RAISE(ABORT, 'stocktake_active_limit_reached');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_StocktakeSessions_BlockDuplicateInProgress"
                BEFORE INSERT ON "StocktakeSessions"
                WHEN NEW."Status" = 'InProgress'
                  AND EXISTS (
                      SELECT 1
                      FROM "StocktakeSessions"
                      WHERE "Status" = 'InProgress'
                        AND lower(trim("Name")) = lower(trim(NEW."Name"))
                        AND "Scope" = NEW."Scope"
                        AND "ScopeLocationId" IS NEW."ScopeLocationId"
                        AND "ScopeCategoryId" IS NEW."ScopeCategoryId")
                BEGIN
                    SELECT RAISE(ABORT, 'stocktake_duplicate_active');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAssignments_ActivePerson_Insert"
                BEFORE INSERT ON "AssetAssignments"
                WHEN NEW."PersonId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "People"
                      WHERE "Id" = NEW."PersonId" AND "IsActive" = 1)
                BEGIN
                    SELECT RAISE(ABORT, 'inactive_assignment_person');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAssignments_ActiveDepartment_Insert"
                BEFORE INSERT ON "AssetAssignments"
                WHEN NEW."DepartmentId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "Departments"
                      WHERE "Id" = NEW."DepartmentId" AND "IsActive" = 1)
                BEGIN
                    SELECT RAISE(ABORT, 'inactive_assignment_department');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAssignments_ActiveTarget_Update"
                BEFORE UPDATE OF "PersonId", "DepartmentId" ON "AssetAssignments"
                WHEN (NEW."PersonId" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "People"
                          WHERE "Id" = NEW."PersonId" AND "IsActive" = 1))
                  OR (NEW."DepartmentId" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "Departments"
                          WHERE "Id" = NEW."DepartmentId" AND "IsActive" = 1))
                BEGIN
                    SELECT RAISE(ABORT, 'inactive_assignment_target');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_People_BlockDeactivateWithOpenAssignments"
                BEFORE UPDATE OF "IsActive" ON "People"
                WHEN OLD."IsActive" = 1
                  AND NEW."IsActive" = 0
                  AND EXISTS (
                      SELECT 1
                      FROM "AssetAssignments"
                      WHERE "PersonId" = OLD."Id" AND "ReturnedAtUtc" IS NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'person_has_open_assignments');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Departments_BlockDeactivateWithOpenAssignments"
                BEFORE UPDATE OF "IsActive" ON "Departments"
                WHEN OLD."IsActive" = 1
                  AND NEW."IsActive" = 0
                  AND EXISTS (
                      SELECT 1
                      FROM "AssetAssignments"
                      WHERE "DepartmentId" = OLD."Id" AND "ReturnedAtUtc" IS NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'department_has_open_assignments');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_LeaverCases_BlockCompletionWithOpenCustody"
                BEFORE UPDATE OF "Status" ON "LeaverCases"
                WHEN NEW."Status" = 'Completed'
                  AND OLD."Status" <> 'Completed'
                  AND EXISTS (
                      SELECT 1
                      FROM "AssetAssignments"
                      WHERE "PersonId" = NEW."PersonId"
                        AND "ReturnedAtUtc" IS NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'leaver_has_open_custody');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAssignments_BlockLeaverTarget_Insert"
                BEFORE INSERT ON "AssetAssignments"
                WHEN NEW."PersonId" IS NOT NULL
                  AND NEW."ReturnedAtUtc" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "LeaverCases"
                      WHERE "PersonId" = NEW."PersonId"
                        AND "Status" IN ('Open', 'Completed'))
                BEGIN
                    SELECT RAISE(ABORT, 'assignment_target_is_leaver');
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_AssetAssignments_BlockLeaverTarget_Update"
                BEFORE UPDATE OF "PersonId", "ReturnedAtUtc" ON "AssetAssignments"
                WHEN NEW."PersonId" IS NOT NULL
                  AND NEW."ReturnedAtUtc" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "LeaverCases"
                      WHERE "PersonId" = NEW."PersonId"
                        AND "Status" IN ('Open', 'Completed'))
                BEGIN
                    SELECT RAISE(ABORT, 'assignment_target_is_leaver');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_StocktakeSessions_BlockDuplicateInProgress";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_StocktakeSessions_LimitInProgress";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetBulkChangeJobs_LimitActivePerUser";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAttachments_LimitPerAsset";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetBundleMembers_BlockTerminalAsset_Update";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetBundleMembers_BlockTerminalAsset_Insert";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_Assets_BlockTerminalStatusWithBundle";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_SavedAssetSearches_LimitActivePerUser";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAssignments_BlockLeaverTarget_Update";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAssignments_BlockLeaverTarget_Insert";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_LeaverCases_BlockCompletionWithOpenCustody";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_Departments_BlockDeactivateWithOpenAssignments";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_People_BlockDeactivateWithOpenAssignments";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAssignments_ActiveTarget_Update";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAssignments_ActiveDepartment_Insert";""");
            migrationBuilder.Sql(
                """DROP TRIGGER IF EXISTS "TR_AssetAssignments_ActivePerson_Insert";""");
        }
    }
}
