using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMilestone5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetImportJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: false),
                    TargetAssetCount = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyReconciliationRuns", x => x.Id);
                    table.CheckConstraint("CK_LegacyReconciliationRuns_TargetAssetCount", "\"TargetAssetCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationRuns_AssetImportJobs_AssetImportJobId",
                        column: x => x.AssetImportJobId,
                        principalTable: "AssetImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyReconciliationSignOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReconciliationRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReportFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    SignedExceptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyReconciliationSignOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationSignOffs_LegacyReconciliationRuns_ReconciliationRunId",
                        column: x => x.ReconciliationRunId,
                        principalTable: "LegacyReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyReconciliationSignOffs_Users_SignedByUserId",
                        column: x => x.SignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_AssetImportJobId",
                table: "LegacyReconciliationRuns",
                column: "AssetImportJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_CreatedByUserId",
                table: "LegacyReconciliationRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationRuns_ReportFingerprint",
                table: "LegacyReconciliationRuns",
                column: "ReportFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationSignOffs_ReconciliationRunId",
                table: "LegacyReconciliationSignOffs",
                column: "ReconciliationRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyReconciliationSignOffs_SignedByUserId",
                table: "LegacyReconciliationSignOffs",
                column: "SignedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyReconciliationSignOffs");

            migrationBuilder.DropTable(
                name: "LegacyReconciliationRuns");
        }
    }
}
