using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCollectorSessionUniquenessToMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.CreateIndex(
                name: "ux_collector_sessions_active_market",
                schema: "data_collection",
                table: "collector_sessions",
                column: "market_id",
                unique: true,
                filter: "\"status\" IN (0, 1, 2, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_collector_sessions_active_market",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.AddColumn<short>(
                name: "exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.CreateIndex(
                name: "ux_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions",
                column: "exclusive_slot",
                unique: true,
                filter: "\"status\" IN (0, 1, 2, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions",
                sql: "\"exclusive_slot\" = 1");
        }
    }
}
