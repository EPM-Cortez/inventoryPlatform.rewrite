using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportPreviewBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportPreviewBudgetGates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportPreviewBudgetGates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportPreviewReservations",
                columns: table => new
                {
                    PayloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PurgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportPreviewReservations", x => new { x.Kind, x.PayloadId });
                    table.CheckConstraint("CK_ImportPreviewReservations_Bytes", "\"PayloadBytes\" >= 0");
                    table.CheckConstraint("CK_ImportPreviewReservations_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_ImportPreviewReservations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ImportPreviewBudgetGates",
                columns: new[] { "Id", "Revision" },
                values: new object[] { 1, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_ImportPreviewReservations_CreatedByUserId_ExpiresAtUtc",
                table: "ImportPreviewReservations",
                columns: new[] { "CreatedByUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportPreviewReservations_ExpiresAtUtc_PurgedAtUtc_PreservedAtUtc",
                table: "ImportPreviewReservations",
                columns: new[] { "ExpiresAtUtc", "PurgedAtUtc", "PreservedAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO "ImportPreviewReservations"
                    ("Kind", "PayloadId", "CreatedByUserId", "PayloadBytes",
                     "CreatedAtUtc", "ExpiresAtUtc", "PreservedAtUtc")
                SELECT 'Asset', job."Id", job."CreatedByUserId",
                       length(CAST(job."ValidatedRowsJson" AS BLOB)) +
                       length(CAST(job."ErrorsJson" AS BLOB)),
                       job."CreatedAtUtc",
                       date(job."CreatedAtUtc", '+7 days') || substr(job."CreatedAtUtc", 11),
                       (SELECT signoff."SignedAtUtc"
                        FROM "LegacyReconciliationRuns" AS run
                        JOIN "LegacyReconciliationSignOffs" AS signoff
                          ON signoff."ReconciliationRunId" = run."Id"
                        WHERE run."AssetImportJobId" = job."Id" LIMIT 1)
                FROM "AssetImportJobs" AS job;
                """);
            migrationBuilder.Sql("""
                INSERT INTO "ImportPreviewReservations"
                    ("Kind", "PayloadId", "CreatedByUserId", "PayloadBytes",
                     "CreatedAtUtc", "ExpiresAtUtc")
                SELECT 'ReferenceData', job."Id", job."CreatedByUserId",
                       length(CAST(job."ValidatedRowsJson" AS BLOB)) +
                       length(CAST(job."ErrorsJson" AS BLOB)),
                       job."CreatedAtUtc",
                       date(job."CreatedAtUtc", '+7 days') || substr(job."CreatedAtUtc", 11)
                FROM "ReferenceDataImportJobs" AS job;
                """);
            migrationBuilder.Sql("""
                INSERT INTO "ImportPreviewReservations"
                    ("Kind", "PayloadId", "CreatedByUserId", "PayloadBytes",
                     "CreatedAtUtc", "ExpiresAtUtc", "PreservedAtUtc")
                SELECT 'LegacyReport', run."Id", run."CreatedByUserId",
                       length(CAST(run."ReportJson" AS BLOB)),
                       run."CreatedAtUtc",
                       date(run."CreatedAtUtc", '+7 days') || substr(run."CreatedAtUtc", 11),
                       (SELECT signoff."SignedAtUtc"
                        FROM "LegacyReconciliationSignOffs" AS signoff
                        WHERE signoff."ReconciliationRunId" = run."Id" LIMIT 1)
                FROM "LegacyReconciliationRuns" AS run;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportPreviewBudgetGates");

            migrationBuilder.DropTable(
                name: "ImportPreviewReservations");
        }
    }
}
