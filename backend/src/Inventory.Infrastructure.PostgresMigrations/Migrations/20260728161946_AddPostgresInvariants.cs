using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.PostgresMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgresInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_school_profile_singleton()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('inventory:school-profile', 0));
                    IF EXISTS (SELECT 1 FROM "SchoolProfile") THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'school_profile_singleton';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_SchoolProfile_Singleton_Insert"
                BEFORE INSERT ON "SchoolProfile"
                FOR EACH ROW EXECUTE FUNCTION inventory_school_profile_singleton();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_managed_agent_asset_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."LinkedAssetId" IS NOT NULL THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:asset:' || NEW."LinkedAssetId"::text,
                                0));
                        IF EXISTS (
                            SELECT 1
                            FROM "Assets"
                            WHERE "Id" = NEW."LinkedAssetId"
                              AND "Status" IN ('Retired', 'Disposed'))
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'agent_asset_terminal';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Insert"
                BEFORE INSERT ON "ManagedAgents"
                FOR EACH ROW EXECUTE FUNCTION inventory_managed_agent_asset_guard();

                CREATE TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Update"
                BEFORE UPDATE OF "LinkedAssetId" ON "ManagedAgents"
                FOR EACH ROW EXECUTE FUNCTION inventory_managed_agent_asset_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_asset_terminal_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."Status" IN ('Retired', 'Disposed') THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:asset:' || OLD."Id"::text,
                                0));
                        IF EXISTS (
                            SELECT 1
                            FROM "ManagedAgents"
                            WHERE "LinkedAssetId" = OLD."Id")
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'asset_has_linked_agent';
                        END IF;
                        IF EXISTS (
                            SELECT 1
                            FROM "AssetBundleMembers"
                            WHERE "AssetId" = OLD."Id"
                              AND "IsActive")
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'asset_has_active_bundle';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Assets_BlockTerminalStatus"
                BEFORE UPDATE OF "Status" ON "Assets"
                FOR EACH ROW EXECUTE FUNCTION inventory_asset_terminal_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_bundle_member_asset_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."IsActive" THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:asset:' || NEW."AssetId"::text,
                                0));
                        IF EXISTS (
                            SELECT 1
                            FROM "Assets"
                            WHERE "Id" = NEW."AssetId"
                              AND "Status" IN ('Retired', 'Disposed'))
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'bundle_asset_terminal';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_AssetBundleMembers_BlockTerminalAsset_Insert"
                BEFORE INSERT ON "AssetBundleMembers"
                FOR EACH ROW EXECUTE FUNCTION inventory_bundle_member_asset_guard();

                CREATE TRIGGER "TR_AssetBundleMembers_BlockTerminalAsset_Update"
                BEFORE UPDATE OF "AssetId", "IsActive" ON "AssetBundleMembers"
                FOR EACH ROW EXECUTE FUNCTION inventory_bundle_member_asset_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_saved_search_quota()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."ArchivedAtUtc" IS NULL THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:saved-search:' || NEW."UserId"::text,
                                0));
                        IF (
                            SELECT COUNT(*)
                            FROM "SavedAssetSearches"
                            WHERE "UserId" = NEW."UserId"
                              AND "ArchivedAtUtc" IS NULL
                        ) >= 50
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'saved_search_limit_reached';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_SavedAssetSearches_LimitActivePerUser"
                BEFORE INSERT ON "SavedAssetSearches"
                FOR EACH ROW EXECUTE FUNCTION inventory_saved_search_quota();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_attachment_quota()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended(
                            'inventory:attachment:' || NEW."AssetId"::text,
                            0));
                    IF (
                        SELECT COUNT(*)
                        FROM "AssetAttachments"
                        WHERE "AssetId" = NEW."AssetId"
                    ) >= 100
                    OR COALESCE((
                        SELECT SUM("SizeBytes")
                        FROM "AssetAttachments"
                        WHERE "AssetId" = NEW."AssetId"
                    ), 0) + NEW."SizeBytes" > 262144000
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'attachment_quota_exceeded';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_AssetAttachments_LimitPerAsset"
                BEFORE INSERT ON "AssetAttachments"
                FOR EACH ROW EXECUTE FUNCTION inventory_attachment_quota();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_bulk_preview_quota()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."Status" = 'Previewed'
                       AND NEW."ExpiresAtUtc" > CURRENT_TIMESTAMP
                    THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:bulk-preview:' ||
                                NEW."CreatedByUserId"::text,
                                0));
                        IF (
                            SELECT COUNT(*)
                            FROM "AssetBulkChangeJobs"
                            WHERE "CreatedByUserId" = NEW."CreatedByUserId"
                              AND "Status" = 'Previewed'
                              AND "ExpiresAtUtc" > CURRENT_TIMESTAMP
                        ) >= 20
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'bulk_preview_limit_reached';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_AssetBulkChangeJobs_LimitActivePerUser"
                BEFORE INSERT ON "AssetBulkChangeJobs"
                FOR EACH ROW EXECUTE FUNCTION inventory_bulk_preview_quota();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_stocktake_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW."Status" = 'InProgress' THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended('inventory:stocktakes', 0));
                        IF (
                            SELECT COUNT(*)
                            FROM "StocktakeSessions"
                            WHERE "Status" = 'InProgress'
                        ) >= 10
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'stocktake_active_limit_reached';
                        END IF;
                        IF EXISTS (
                            SELECT 1
                            FROM "StocktakeSessions"
                            WHERE "Status" = 'InProgress'
                              AND lower(btrim("Name")) =
                                  lower(btrim(NEW."Name"))
                              AND "Scope" = NEW."Scope"
                              AND "ScopeLocationId" IS NOT DISTINCT FROM
                                  NEW."ScopeLocationId"
                              AND "ScopeCategoryId" IS NOT DISTINCT FROM
                                  NEW."ScopeCategoryId")
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'stocktake_duplicate_active';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_StocktakeSessions_GuardInProgress"
                BEFORE INSERT ON "StocktakeSessions"
                FOR EACH ROW EXECUTE FUNCTION inventory_stocktake_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_assignment_target_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    person_changed boolean;
                    department_changed boolean;
                BEGIN
                    person_changed :=
                        TG_OP = 'INSERT' OR
                        NEW."PersonId" IS DISTINCT FROM OLD."PersonId";
                    department_changed :=
                        TG_OP = 'INSERT' OR
                        NEW."DepartmentId" IS DISTINCT FROM OLD."DepartmentId";

                    IF NEW."PersonId" IS NOT NULL THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:custody:' || NEW."PersonId"::text,
                                0));
                    END IF;
                    IF NEW."DepartmentId" IS NOT NULL THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:department-custody:' ||
                                NEW."DepartmentId"::text,
                                0));
                    END IF;

                    IF person_changed
                       AND NEW."PersonId" IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1
                           FROM "People"
                           WHERE "Id" = NEW."PersonId"
                             AND "IsActive")
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = CASE
                                WHEN TG_OP = 'INSERT'
                                THEN 'inactive_assignment_person'
                                ELSE 'inactive_assignment_target'
                            END;
                    END IF;
                    IF department_changed
                       AND NEW."DepartmentId" IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1
                           FROM "Departments"
                           WHERE "Id" = NEW."DepartmentId"
                             AND "IsActive")
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = CASE
                                WHEN TG_OP = 'INSERT'
                                THEN 'inactive_assignment_department'
                                ELSE 'inactive_assignment_target'
                            END;
                    END IF;
                    IF NEW."PersonId" IS NOT NULL
                       AND NEW."ReturnedAtUtc" IS NULL
                       AND EXISTS (
                           SELECT 1
                           FROM "LeaverCases"
                           WHERE "PersonId" = NEW."PersonId"
                             AND "Status" IN ('Open', 'Completed'))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'assignment_target_is_leaver';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_AssetAssignments_TargetGuard_Insert"
                BEFORE INSERT ON "AssetAssignments"
                FOR EACH ROW EXECUTE FUNCTION inventory_assignment_target_guard();

                CREATE TRIGGER "TR_AssetAssignments_TargetGuard_Update"
                BEFORE UPDATE OF "PersonId", "DepartmentId", "ReturnedAtUtc"
                ON "AssetAssignments"
                FOR EACH ROW EXECUTE FUNCTION inventory_assignment_target_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_person_deactivation_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."IsActive" AND NOT NEW."IsActive" THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:custody:' || OLD."Id"::text,
                                0));
                        IF EXISTS (
                            SELECT 1
                            FROM "AssetAssignments"
                            WHERE "PersonId" = OLD."Id"
                              AND "ReturnedAtUtc" IS NULL)
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'person_has_open_assignments';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_People_BlockDeactivateWithOpenAssignments"
                BEFORE UPDATE OF "IsActive" ON "People"
                FOR EACH ROW EXECUTE FUNCTION inventory_person_deactivation_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_department_deactivation_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."IsActive" AND NOT NEW."IsActive" THEN
                        PERFORM pg_advisory_xact_lock(
                            hashtextextended(
                                'inventory:department-custody:' ||
                                OLD."Id"::text,
                                0));
                        IF EXISTS (
                            SELECT 1
                            FROM "AssetAssignments"
                            WHERE "DepartmentId" = OLD."Id"
                              AND "ReturnedAtUtc" IS NULL)
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                MESSAGE = 'department_has_open_assignments';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Departments_BlockDeactivateWithOpenAssignments"
                BEFORE UPDATE OF "IsActive" ON "Departments"
                FOR EACH ROW EXECUTE FUNCTION inventory_department_deactivation_guard();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION inventory_leaver_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended(
                            'inventory:custody:' || NEW."PersonId"::text,
                            0));
                    IF TG_OP = 'UPDATE'
                       AND NEW."Status" = 'Completed'
                       AND OLD."Status" <> 'Completed'
                       AND EXISTS (
                           SELECT 1
                           FROM "AssetAssignments"
                           WHERE "PersonId" = NEW."PersonId"
                             AND "ReturnedAtUtc" IS NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'leaver_has_open_custody';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_LeaverCases_CustodyGuard_Insert"
                BEFORE INSERT ON "LeaverCases"
                FOR EACH ROW EXECUTE FUNCTION inventory_leaver_guard();

                CREATE TRIGGER "TR_LeaverCases_CustodyGuard_Update"
                BEFORE UPDATE OF "PersonId", "Status" ON "LeaverCases"
                FOR EACH ROW EXECUTE FUNCTION inventory_leaver_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_LeaverCases_CustodyGuard_Update"
                    ON "LeaverCases";
                DROP TRIGGER IF EXISTS "TR_LeaverCases_CustodyGuard_Insert"
                    ON "LeaverCases";
                DROP FUNCTION IF EXISTS inventory_leaver_guard();

                DROP TRIGGER IF EXISTS
                    "TR_Departments_BlockDeactivateWithOpenAssignments"
                    ON "Departments";
                DROP FUNCTION IF EXISTS
                    inventory_department_deactivation_guard();

                DROP TRIGGER IF EXISTS
                    "TR_People_BlockDeactivateWithOpenAssignments"
                    ON "People";
                DROP FUNCTION IF EXISTS inventory_person_deactivation_guard();

                DROP TRIGGER IF EXISTS
                    "TR_AssetAssignments_TargetGuard_Update"
                    ON "AssetAssignments";
                DROP TRIGGER IF EXISTS
                    "TR_AssetAssignments_TargetGuard_Insert"
                    ON "AssetAssignments";
                DROP FUNCTION IF EXISTS inventory_assignment_target_guard();

                DROP TRIGGER IF EXISTS
                    "TR_StocktakeSessions_GuardInProgress"
                    ON "StocktakeSessions";
                DROP FUNCTION IF EXISTS inventory_stocktake_guard();

                DROP TRIGGER IF EXISTS
                    "TR_AssetBulkChangeJobs_LimitActivePerUser"
                    ON "AssetBulkChangeJobs";
                DROP FUNCTION IF EXISTS inventory_bulk_preview_quota();

                DROP TRIGGER IF EXISTS
                    "TR_AssetAttachments_LimitPerAsset"
                    ON "AssetAttachments";
                DROP FUNCTION IF EXISTS inventory_attachment_quota();

                DROP TRIGGER IF EXISTS
                    "TR_SavedAssetSearches_LimitActivePerUser"
                    ON "SavedAssetSearches";
                DROP FUNCTION IF EXISTS inventory_saved_search_quota();

                DROP TRIGGER IF EXISTS
                    "TR_AssetBundleMembers_BlockTerminalAsset_Update"
                    ON "AssetBundleMembers";
                DROP TRIGGER IF EXISTS
                    "TR_AssetBundleMembers_BlockTerminalAsset_Insert"
                    ON "AssetBundleMembers";
                DROP FUNCTION IF EXISTS inventory_bundle_member_asset_guard();

                DROP TRIGGER IF EXISTS "TR_Assets_BlockTerminalStatus"
                    ON "Assets";
                DROP FUNCTION IF EXISTS inventory_asset_terminal_guard();

                DROP TRIGGER IF EXISTS
                    "TR_ManagedAgents_BlockTerminalAssetLink_Update"
                    ON "ManagedAgents";
                DROP TRIGGER IF EXISTS
                    "TR_ManagedAgents_BlockTerminalAssetLink_Insert"
                    ON "ManagedAgents";
                DROP FUNCTION IF EXISTS inventory_managed_agent_asset_guard();

                DROP TRIGGER IF EXISTS
                    "TR_SchoolProfile_Singleton_Insert"
                    ON "SchoolProfile";
                DROP FUNCTION IF EXISTS inventory_school_profile_singleton();
                """);
        }
    }
}
