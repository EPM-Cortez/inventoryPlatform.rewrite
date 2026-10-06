using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentReplayNonces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentRequestNonces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ContextId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NonceHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 44, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRequestNonces", x => x.Id);
                    table.CheckConstraint("CK_AgentRequestNonces_Expiry", "\"ExpiresAtUtc\" > \"ReceivedAtUtc\"");
                    table.CheckConstraint("CK_AgentRequestNonces_Scope", "\"Scope\" IN ('Enrollment', 'CheckIn')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRequestNonces_ExpiresAtUtc",
                table: "AgentRequestNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_AgentRequestNonces_ScopeContextNonce",
                table: "AgentRequestNonces",
                columns: new[] { "Scope", "ContextId", "NonceHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRequestNonces");
        }
    }
}
