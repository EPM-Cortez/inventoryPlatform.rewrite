using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedEndpointAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentEnrollmentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 44, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxEnrollments = table.Column<int>(type: "INTEGER", nullable: false),
                    EnrolledCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireTpm = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedHostnameSuffix = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEnrollmentProfiles", x => x.Id);
                    table.CheckConstraint("CK_AgentEnrollmentProfiles_EnrollmentCount", "\"EnrolledCount\" >= 0 AND \"MaxEnrollments\" >= 1 AND \"MaxEnrollments\" <= 5000 AND \"EnrolledCount\" <= \"MaxEnrollments\"");
                    table.CheckConstraint("CK_AgentEnrollmentProfiles_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AgentEnrollmentProfiles_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkedAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeviceIdNormalized = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    KeyThumbprintSha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    KeyProtection = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    HostnameNormalized = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SerialNumberNormalized = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BiosUuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BiosUuidNormalized = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    MatchState = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MatchReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EnrolledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckInAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCollectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAgentVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LastSeenIpAddress = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedAgents", x => x.Id);
                    table.CheckConstraint("CK_ManagedAgents_LastSequence", "\"LastSequence\" >= 0");
                    table.CheckConstraint("CK_ManagedAgents_Revocation", "(\"RevokedAtUtc\" IS NULL AND \"RevokedReason\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ManagedAgents_AgentEnrollmentProfiles_EnrollmentProfileId",
                        column: x => x.EnrollmentProfileId,
                        principalTable: "AgentEnrollmentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagedAgents_Assets_LinkedAssetId",
                        column: x => x.LinkedAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedAgentSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BodySha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 44, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BiosUuid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AgentVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedAgentSnapshots", x => x.Id);
                    table.CheckConstraint("CK_ManagedAgentSnapshots_Sequence", "\"Sequence\" >= 1");
                    table.ForeignKey(
                        name: "FK_ManagedAgentSnapshots_ManagedAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "ManagedAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_CreatedByUserId",
                table: "AgentEnrollmentProfiles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_ExpiresAtUtc",
                table: "AgentEnrollmentProfiles",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEnrollmentProfiles_RevokedAtUtc",
                table: "AgentEnrollmentProfiles",
                column: "RevokedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_AgentEnrollmentProfiles_TokenHash",
                table: "AgentEnrollmentProfiles",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_EnrollmentProfileId",
                table: "ManagedAgents",
                column: "EnrollmentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_HostnameNormalized",
                table: "ManagedAgents",
                column: "HostnameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgents_LastCheckInAtUtc",
                table: "ManagedAgents",
                column: "LastCheckInAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_DeviceIdNormalized",
                table: "ManagedAgents",
                column: "DeviceIdNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_KeyThumbprint",
                table: "ManagedAgents",
                column: "KeyThumbprintSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgents_LinkedAssetId",
                table: "ManagedAgents",
                column: "LinkedAssetId",
                unique: true,
                filter: "\"LinkedAssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAgentSnapshots_AgentId_ReceivedAtUtc",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgentSnapshots_AgentSequence",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ManagedAgentSnapshots_AgentSnapshot",
                table: "ManagedAgentSnapshots",
                columns: new[] { "AgentId", "SnapshotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedAgentSnapshots");

            migrationBuilder.DropTable(
                name: "ManagedAgents");

            migrationBuilder.DropTable(
                name: "AgentEnrollmentProfiles");
        }
    }
}
