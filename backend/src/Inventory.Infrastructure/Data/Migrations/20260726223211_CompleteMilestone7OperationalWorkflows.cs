using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMilestone7OperationalWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedReturnOn",
                table: "AssetAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssetAssignments_ExpectedReturnOn",
                table: "AssetAssignments",
                sql: "\"ExpectedReturnOn\" IS NULL OR \"ExpectedReturnOn\" > '0001-01-01'");

            migrationBuilder.CreateTable(
                name: "AssetAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAttachments", x => x.Id);
                    table.CheckConstraint("CK_AssetAttachments_Archive", "(\"ArchivedAtUtc\" IS NULL AND \"ArchivedByUserId\" IS NULL) OR (\"ArchivedAtUtc\" IS NOT NULL AND \"ArchivedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetAttachments_ContentSize", "\"SizeBytes\" >= 1 AND \"SizeBytes\" <= 10485760 AND length(\"Content\") = \"SizeBytes\"");
                    table.CheckConstraint("CK_AssetAttachments_FileTypeAndHash", "\"ContentType\" IN ('application/pdf', 'image/jpeg', 'image/png') AND length(\"Sha256\") = 64");
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Users_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAttachments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBulkChangeJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OperationJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ItemsJson = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AssetCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBulkChangeJobs", x => x.Id);
                    table.CheckConstraint("CK_AssetBulkChangeJobs_AssetCount", "\"AssetCount\" BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_AssetBulkChangeJobs_Commit", "(\"Status\" = 'Previewed' AND \"CommittedAtUtc\" IS NULL) OR (\"Status\" = 'Committed' AND \"CommittedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetBulkChangeJobs_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AssetBulkChangeJobs_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    NameNormalized = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetTagPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NumericWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    NextNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTagPolicies", x => x.Id);
                    table.CheckConstraint("CK_AssetTagPolicies_Sequence", "\"NumericWidth\" BETWEEN 2 AND 12 AND \"NextNumber\" >= 1 AND length(CAST(\"NextNumber\" - 1 AS TEXT)) <= \"NumericWidth\"");
                    table.CheckConstraint("CK_AssetTagPolicies_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "LeaverCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeavingOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaverCases", x => x.Id);
                    table.CheckConstraint("CK_LeaverCases_Lifecycle", "(\"Status\" = 'Open' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL) OR (\"Status\" = 'Rehired' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LeaverCases_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaverCases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SavedAssetSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    FilterJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedAssetSearches", x => x.Id);
                    table.CheckConstraint("CK_SavedAssetSearches_ArchivedDefault", "\"ArchivedAtUtc\" IS NULL OR \"IsDefault\" = 0");
                    table.CheckConstraint("CK_SavedAssetSearches_FilterSize", "length(\"FilterJson\") BETWEEN 2 AND 16000");
                    table.CheckConstraint("CK_SavedAssetSearches_SchemaVersion", "\"SchemaVersion\" = 1");
                    table.ForeignKey(
                        name: "FK_SavedAssetSearches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ScopeLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeSessions", x => x.Id);
                    table.CheckConstraint("CK_StocktakeSessions_Lifecycle", "(\"Status\" = 'InProgress' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL AND \"CompletedByDisplayName\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayName\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CompletedAtUtc\" IS NULL AND \"CompletedByUserId\" IS NULL AND \"CompletedByDisplayName\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancelledByDisplayName\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_StocktakeSessions_Scope", "(\"Scope\" = 'All' AND \"ScopeLocationId\" IS NULL AND \"ScopeCategoryId\" IS NULL) OR (\"Scope\" = 'Location' AND \"ScopeLocationId\" IS NOT NULL AND \"ScopeCategoryId\" IS NULL) OR (\"Scope\" = 'Category' AND \"ScopeLocationId\" IS NULL AND \"ScopeCategoryId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_AssetCategories_ScopeCategoryId",
                        column: x => x.ScopeCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Locations_ScopeLocationId",
                        column: x => x.ScopeLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBundleMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BundleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OtherRole = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBundleMembers", x => x.Id);
                    table.CheckConstraint("CK_AssetBundleMembers_Lifecycle", "(\"RemovedAtUtc\" IS NULL) OR (\"IsActive\" = 0)");
                    table.CheckConstraint("CK_AssetBundleMembers_OtherRole", "(\"Role\" = 'Other' AND \"OtherRole\" IS NOT NULL) OR (\"Role\" <> 'Other' AND \"OtherRole\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AssetBundleMembers_AssetBundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "AssetBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetBundleMembers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeExpectedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetTag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ExpectedStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ExpectedCondition = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ExpectedLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedLocationCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ExpectedLocationName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeExpectedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeExpectedAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeExpectedAssets_StocktakeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StocktakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetTag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ObservedLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservedLocationCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ObservedLocationName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ObservedCondition = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObservedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Locations_ObservedLocationId",
                        column: x => x.ObservedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_StocktakeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StocktakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StocktakeObservations_Users_ObservedByUserId",
                        column: x => x.ObservedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AssetTagPolicies",
                columns: new[] { "Id", "NextNumber", "NumericWidth", "Prefix", "Version" },
                values: new object[] { 1, 1L, 6, "AST-", new Guid("00000000-0000-0000-0000-000000000701") });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ExpectedReturnOn_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "ExpectedReturnOn", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_ArchivedByUserId",
                table: "AssetAttachments",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_AssetId_ArchivedAtUtc_CreatedAtUtc",
                table: "AssetAttachments",
                columns: new[] { "AssetId", "ArchivedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_AssetId_Sha256",
                table: "AssetAttachments",
                columns: new[] { "AssetId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttachments_CreatedByUserId",
                table: "AssetAttachments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_CreatedByUserId",
                table: "AssetBulkChangeJobs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_Fingerprint",
                table: "AssetBulkChangeJobs",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBulkChangeJobs_Status_ExpiresAtUtc",
                table: "AssetBulkChangeJobs",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetBundleMembers_AssetId_Active",
                table: "AssetBundleMembers",
                column: "AssetId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundleMembers_BundleAsset",
                table: "AssetBundleMembers",
                columns: new[] { "BundleId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundles_CodeNormalized",
                table: "AssetBundles",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AssetBundles_NameNormalized",
                table: "AssetBundles",
                column: "NameNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CancelledByUserId",
                table: "LeaverCases",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CompletedByUserId",
                table: "LeaverCases",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_CreatedByUserId",
                table: "LeaverCases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaverCases_Status_LeavingOn",
                table: "LeaverCases",
                columns: new[] { "Status", "LeavingOn" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaverCases_OpenPerson",
                table: "LeaverCases",
                column: "PersonId",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "UX_SavedAssetSearches_ActiveName",
                table: "SavedAssetSearches",
                columns: new[] { "UserId", "NameNormalized" },
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SavedAssetSearches_DefaultUser",
                table: "SavedAssetSearches",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\" = 1 AND \"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeExpectedAssets_AssetId",
                table: "StocktakeExpectedAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeExpectedAssets_SessionId_AssetTag",
                table: "StocktakeExpectedAssets",
                columns: new[] { "SessionId", "AssetTag" });

            migrationBuilder.CreateIndex(
                name: "UX_StocktakeExpectedAssets_SessionAsset",
                table: "StocktakeExpectedAssets",
                columns: new[] { "SessionId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_AssetId",
                table: "StocktakeObservations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_ObservedByUserId",
                table: "StocktakeObservations",
                column: "ObservedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_ObservedLocationId",
                table: "StocktakeObservations",
                column: "ObservedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeObservations_SessionId_ObservedAtUtc",
                table: "StocktakeObservations",
                columns: new[] { "SessionId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StocktakeObservations_SessionAsset",
                table: "StocktakeObservations",
                columns: new[] { "SessionId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_CancelledByUserId",
                table: "StocktakeSessions",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_CompletedByUserId",
                table: "StocktakeSessions",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_ScopeCategoryId",
                table: "StocktakeSessions",
                column: "ScopeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_ScopeLocationId",
                table: "StocktakeSessions",
                column: "ScopeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_StartedByUserId",
                table: "StocktakeSessions",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeSessions_Status_StartedAtUtc",
                table: "StocktakeSessions",
                columns: new[] { "Status", "StartedAtUtc" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetAttachments");

            migrationBuilder.DropTable(
                name: "AssetBulkChangeJobs");

            migrationBuilder.DropTable(
                name: "AssetBundleMembers");

            migrationBuilder.DropTable(
                name: "AssetTagPolicies");

            migrationBuilder.DropTable(
                name: "LeaverCases");

            migrationBuilder.DropTable(
                name: "SavedAssetSearches");

            migrationBuilder.DropTable(
                name: "StocktakeExpectedAssets");

            migrationBuilder.DropTable(
                name: "StocktakeObservations");

            migrationBuilder.DropTable(
                name: "AssetBundles");

            migrationBuilder.DropTable(
                name: "StocktakeSessions");

            migrationBuilder.DropIndex(
                name: "IX_AssetAssignments_ExpectedReturnOn_ReturnedAtUtc",
                table: "AssetAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssetAssignments_ExpectedReturnOn",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "ExpectedReturnOn",
                table: "AssetAssignments");
        }
    }
}
