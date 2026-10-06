using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrepareAssetAssignmentRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
