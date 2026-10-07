using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveReportedSerialNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportedSerialNumber",
                table: "Assets",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EF's SQLite table-rebuild path conflicts with invariant triggers
            // that reference Assets. SQLite supports dropping this standalone
            // nullable column directly without disturbing those triggers.
            migrationBuilder.Sql(
                "ALTER TABLE \"Assets\" DROP COLUMN \"ReportedSerialNumber\"");
        }
    }
}
