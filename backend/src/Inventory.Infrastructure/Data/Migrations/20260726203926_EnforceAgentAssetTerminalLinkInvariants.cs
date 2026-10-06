using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAgentAssetTerminalLinkInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "ManagedAgentSnapshots"
                SET "ContentSha256" = "BodySha256"
                WHERE "ContentSha256" = '';
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Insert"
                BEFORE INSERT ON "ManagedAgents"
                WHEN NEW."LinkedAssetId" IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "Assets"
                      WHERE "Id" = NEW."LinkedAssetId"
                        AND "Status" IN ('Retired', 'Disposed'))
                BEGIN
                    SELECT RAISE(ABORT, 'agent_asset_terminal');
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Update"
                BEFORE UPDATE OF "LinkedAssetId" ON "ManagedAgents"
                WHEN NEW."LinkedAssetId" IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "Assets"
                      WHERE "Id" = NEW."LinkedAssetId"
                        AND "Status" IN ('Retired', 'Disposed'))
                BEGIN
                    SELECT RAISE(ABORT, 'agent_asset_terminal');
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Assets_BlockTerminalStatusWithAgent"
                BEFORE UPDATE OF "Status" ON "Assets"
                WHEN NEW."Status" IN ('Retired', 'Disposed')
                  AND EXISTS (
                      SELECT 1
                      FROM "ManagedAgents"
                      WHERE "LinkedAssetId" = OLD."Id")
                BEGIN
                    SELECT RAISE(ABORT, 'asset_has_linked_agent');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP TRIGGER "TR_Assets_BlockTerminalStatusWithAgent";""");
            migrationBuilder.Sql(
                """DROP TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Update";""");
            migrationBuilder.Sql(
                """DROP TRIGGER "TR_ManagedAgents_BlockTerminalAssetLink_Insert";""");
        }
    }
}
