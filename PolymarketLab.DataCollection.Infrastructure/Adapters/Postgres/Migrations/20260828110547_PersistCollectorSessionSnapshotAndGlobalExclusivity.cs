using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistCollectorSessionSnapshotAndGlobalExclusivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF (
                        SELECT COUNT(*)
                        FROM data_collection.collector_sessions
                        WHERE status IN (0, 1, 2)
                    ) > 1 THEN
                        RAISE EXCEPTION 'Cannot create the global collector slot while multiple legacy sessions are active.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_collector_sessions_active_market",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.AddColumn<string>(
                name: "condition_id",
                schema: "data_collection",
                table: "collector_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "event_ends_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "event_slug",
                schema: "data_collection",
                table: "collector_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "event_starts_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "external_event_id",
                schema: "data_collection",
                table: "collector_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_market_id",
                schema: "data_collection",
                table: "collector_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "market_slug",
                schema: "data_collection",
                table: "collector_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "phase",
                schema: "data_collection",
                table: "collector_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "projection_version",
                schema: "data_collection",
                table: "collector_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subscription_ready_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collector_session_tokens",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome_index = table.Column<int>(type: "integer", nullable: false),
                    token_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collector_session_tokens", x => new { x.session_id, x.outcome_index });
                    table.ForeignKey(
                        name: "FK_collector_session_tokens_collector_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "ux_collector_session_tokens_session_token",
                schema: "data_collection",
                table: "collector_session_tokens",
                columns: new[] { "session_id", "token_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collector_session_tokens",
                schema: "data_collection");

            migrationBuilder.DropIndex(
                name: "ux_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_sessions_exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "condition_id",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "event_ends_at",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "event_slug",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "event_starts_at",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "exclusive_slot",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "external_event_id",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "external_market_id",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "market_slug",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "phase",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "projection_version",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "subscription_ready_at",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.CreateIndex(
                name: "ux_collector_sessions_active_market",
                schema: "data_collection",
                table: "collector_sessions",
                column: "market_id",
                unique: true,
                filter: "\"status\" IN (0, 1, 2)");
        }
    }
}
