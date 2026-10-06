using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    SnapshotHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorRows = table.Column<int>(type: "INTEGER", nullable: false),
                    CreateRows = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdateRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidatedRowsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CommittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommittedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CommittedCreateRows = table.Column<int>(type: "INTEGER", nullable: false),
                    CommittedUpdateRows = table.Column<int>(type: "INTEGER", nullable: false),
                    CommittedUnchangedRows = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetImportJobs", x => x.Id);
                    table.CheckConstraint("CK_AssetImportJobs_CommitCounts", "\"CommittedCreateRows\" >= 0 AND \"CommittedUpdateRows\" >= 0 AND \"CommittedUnchangedRows\" >= 0 AND ((\"Status\" = 'Committed' AND \"CommittedCreateRows\" + \"CommittedUpdateRows\" + \"CommittedUnchangedRows\" = \"ValidRows\") OR (\"Status\" <> 'Committed' AND \"CommittedCreateRows\" = 0 AND \"CommittedUpdateRows\" = 0 AND \"CommittedUnchangedRows\" = 0))");
                    table.CheckConstraint("CK_AssetImportJobs_CommitState", "(\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL AND \"CommittedByUserId\" IS NOT NULL AND \"CommittedByDisplayName\" IS NOT NULL) OR (\"Status\" <> 'Committed' AND \"CommittedAtUtc\" IS NULL AND \"CommittedByUserId\" IS NULL AND \"CommittedByDisplayName\" IS NULL)");
                    table.CheckConstraint("CK_AssetImportJobs_Counts", "\"TotalRows\" >= 0 AND \"ValidRows\" >= 0 AND \"ErrorRows\" >= 0 AND \"CreateRows\" >= 0 AND \"UpdateRows\" >= 0 AND \"ValidRows\" + \"ErrorRows\" = \"TotalRows\" AND \"CreateRows\" + \"UpdateRows\" = \"ValidRows\"");
                    table.ForeignKey(
                        name: "FK_AssetImportJobs_Users_CommittedByUserId",
                        column: x => x.CommittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetImportJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CommittedByUserId",
                table: "AssetImportJobs",
                column: "CommittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CreatedAtUtc",
                table: "AssetImportJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_CreatedByUserId",
                table: "AssetImportJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetImportJobs_Status_CreatedAtUtc",
                table: "AssetImportJobs",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetImportJobs");
        }
    }
}
