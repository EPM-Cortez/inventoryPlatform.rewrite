using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleDepartmentsAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CodeNormalized = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ExternalIdNormalized = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AssignedCondition = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AssignedLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignmentNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReturnedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReturnedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReturnCondition = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ReturnLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReturnNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAssignments", x => x.Id);
                    table.CheckConstraint("CK_AssetAssignments_ExactlyOneTarget", "(\"PersonId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR (\"PersonId\" IS NULL AND \"DepartmentId\" IS NOT NULL)");
                    table.CheckConstraint("CK_AssetAssignments_ReturnedFields", "(\"ReturnedAtUtc\" IS NULL AND \"ReturnedByUserId\" IS NULL) OR (\"ReturnedAtUtc\" IS NOT NULL AND \"ReturnedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Locations_AssignedLocationId",
                        column: x => x.AssignedLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Locations_ReturnLocationId",
                        column: x => x.ReturnLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Users_ReturnedByUserId",
                        column: x => x.ReturnedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssetId_AssignedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "AssetId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssignedByUserId",
                table: "AssetAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssignedLocationId",
                table: "AssetAssignments",
                column: "AssignedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_DepartmentId_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "DepartmentId", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_PersonId_ReturnedAtUtc",
                table: "AssetAssignments",
                columns: new[] { "PersonId", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ReturnedByUserId",
                table: "AssetAssignments",
                column: "ReturnedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ReturnLocationId",
                table: "AssetAssignments",
                column: "ReturnLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_AssetAssignments_OpenAsset",
                table: "AssetAssignments",
                column: "AssetId",
                unique: true,
                filter: "\"ReturnedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_IsActive_Name",
                table: "Departments",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_Departments_CodeNormalized",
                table: "Departments",
                column: "CodeNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_IsActive_DisplayName",
                table: "People",
                columns: new[] { "IsActive", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "UX_People_ExternalIdNormalized",
                table: "People",
                column: "ExternalIdNormalized",
                unique: true,
                filter: "\"ExternalIdNormalized\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetAssignments");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "People");
        }
    }
}
