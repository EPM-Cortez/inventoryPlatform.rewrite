using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQrIdentifiersAndWarranties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QrIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TokenVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssuedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevokedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrIdentifiers", x => x.Id);
                    table.CheckConstraint("CK_QrIdentifiers_RevocationFields", "(\"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_QrIdentifiers_TokenVersion", "\"TokenVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Users_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QrIdentifiers_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarrantyCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CoverageType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarrantyCoverages", x => x.Id);
                    table.CheckConstraint("CK_WarrantyCoverages_ArchivedPrimary", "\"ArchivedAtUtc\" IS NULL OR \"IsPrimary\" = 0");
                    table.CheckConstraint("CK_WarrantyCoverages_DateRange", "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_WarrantyCoverages_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QrIdentifiers_IssuedByUserId",
                table: "QrIdentifiers",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QrIdentifiers_RevokedByUserId",
                table: "QrIdentifiers",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_ActiveAsset",
                table: "QrIdentifiers",
                column: "AssetId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_AssetVersion",
                table: "QrIdentifiers",
                columns: new[] { "AssetId", "TokenVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QrIdentifiers_TokenHash",
                table: "QrIdentifiers",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarrantyCoverages_AssetId_EndDate",
                table: "WarrantyCoverages",
                columns: new[] { "AssetId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "UX_WarrantyCoverages_ActivePrimary",
                table: "WarrantyCoverages",
                column: "AssetId",
                unique: true,
                filter: "\"IsPrimary\" = 1 AND \"ArchivedAtUtc\" IS NULL");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_SchoolProfile_Singleton_Insert"
                BEFORE INSERT ON "SchoolProfile"
                WHEN EXISTS (SELECT 1 FROM "SchoolProfile")
                BEGIN
                    SELECT RAISE(ABORT, 'school_profile_singleton');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_SchoolProfile_Singleton_Insert";
                """);

            migrationBuilder.DropTable(
                name: "QrIdentifiers");

            migrationBuilder.DropTable(
                name: "WarrantyCoverages");
        }
    }
}
