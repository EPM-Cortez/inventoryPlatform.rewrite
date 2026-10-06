using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAgentLifecycleAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentRequestNonces_Scope",
                table: "AgentRequestNonces");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "ManagedAgentSnapshots",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "ContentSha256",
                table: "ManagedAgentSnapshots",
                type: "TEXT",
                fixedLength: true,
                maxLength: 44,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CheckInBurstCount",
                table: "ManagedAgents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInBurstWindowStartedAtUtc",
                table: "ManagedAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckInQuotaCount",
                table: "ManagedAgents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInQuotaWindowStartedAtUtc",
                table: "ManagedAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KeyReplacedAtUtc",
                table: "ManagedAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyReplacementCount",
                table: "ManagedAgents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AgentKeyRecoveryAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 44, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RequireTpm = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpectedAgentVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsumedKeyThumbprintSha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevokedReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentKeyRecoveryAuthorizations", x => x.Id);
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_ClosedState", "NOT (\"ConsumedAtUtc\" IS NOT NULL AND \"RevokedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Consumption", "(\"ConsumedAtUtc\" IS NULL AND \"ConsumedKeyThumbprintSha256\" IS NULL) OR (\"ConsumedAtUtc\" IS NOT NULL AND \"ConsumedKeyThumbprintSha256\" IS NOT NULL)");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_AgentKeyRecoveryAuthorizations_Revocation", "(\"RevokedAtUtc\" IS NULL AND \"RevokedByUserId\" IS NULL AND \"RevokedReason\" IS NULL) OR (\"RevokedAtUtc\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokedReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_ManagedAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "ManagedAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentKeyRecoveryAuthorizations_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManagedAgents_CheckInCapacity",
                table: "ManagedAgents",
                sql: "\"CheckInBurstCount\" >= 0 AND \"CheckInQuotaCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManagedAgents_KeyReplacementCount",
                table: "ManagedAgents",
                sql: "\"KeyReplacementCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentRequestNonces_Scope",
                table: "AgentRequestNonces",
                sql: "\"Scope\" IN ('Enrollment', 'KeyRecovery', 'CheckIn')");

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_CreatedByUserId",
                table: "AgentKeyRecoveryAuthorizations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_ExpiresAtUtc",
                table: "AgentKeyRecoveryAuthorizations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentKeyRecoveryAuthorizations_RevokedByUserId",
                table: "AgentKeyRecoveryAuthorizations",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AgentKeyRecoveryAuthorizations_PendingAgent",
                table: "AgentKeyRecoveryAuthorizations",
                column: "AgentId",
                unique: true,
                filter: "\"ConsumedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AgentKeyRecoveryAuthorizations_TokenHash",
                table: "AgentKeyRecoveryAuthorizations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentKeyRecoveryAuthorizations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ManagedAgents_CheckInCapacity",
                table: "ManagedAgents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ManagedAgents_KeyReplacementCount",
                table: "ManagedAgents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentRequestNonces_Scope",
                table: "AgentRequestNonces");

            migrationBuilder.DropColumn(
                name: "ContentSha256",
                table: "ManagedAgentSnapshots");

            migrationBuilder.DropColumn(
                name: "CheckInBurstCount",
                table: "ManagedAgents");

            migrationBuilder.DropColumn(
                name: "CheckInBurstWindowStartedAtUtc",
                table: "ManagedAgents");

            migrationBuilder.DropColumn(
                name: "CheckInQuotaCount",
                table: "ManagedAgents");

            migrationBuilder.DropColumn(
                name: "CheckInQuotaWindowStartedAtUtc",
                table: "ManagedAgents");

            migrationBuilder.DropColumn(
                name: "KeyReplacedAtUtc",
                table: "ManagedAgents");

            migrationBuilder.DropColumn(
                name: "KeyReplacementCount",
                table: "ManagedAgents");

            migrationBuilder.Sql(
                """
                DELETE FROM "ManagedAgentSnapshots"
                WHERE "PayloadJson" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "ManagedAgentSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentRequestNonces_Scope",
                table: "AgentRequestNonces",
                sql: "\"Scope\" IN ('Enrollment', 'CheckIn')");
        }
    }
}
