using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistResolutionConsensus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolution_confirmed_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "resolution_connection_epoch",
                schema: "data_collection",
                table: "collector_sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolution_signaled_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "winning_outcome",
                schema: "data_collection",
                table: "collector_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "winning_token_id",
                schema: "data_collection",
                table: "collector_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "resolution_observations",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    winner_token_id = table.Column<string>(type: "text", nullable: true),
                    winner_outcome = table.Column<string>(type: "text", nullable: true),
                    external_event_id = table.Column<string>(type: "text", nullable: true),
                    event_slug = table.Column<string>(type: "text", nullable: true),
                    external_market_id = table.Column<string>(type: "text", nullable: true),
                    market_slug = table.Column<string>(type: "text", nullable: true),
                    condition_id = table.Column<string>(type: "text", nullable: true),
                    closed = table.Column<bool>(type: "boolean", nullable: true),
                    accepting_orders = table.Column<bool>(type: "boolean", nullable: true),
                    uma_resolution_status = table.Column<string>(type: "text", nullable: true),
                    external_closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    raw_message_id = table.Column<long>(type: "bigint", nullable: true),
                    raw_item_index = table.Column<int>(type: "integer", nullable: true),
                    connection_epoch = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resolution_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_resolution_observations_collector_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resolution_observation_outcomes",
                schema: "data_collection",
                columns: table => new
                {
                    observation_id = table.Column<long>(type: "bigint", nullable: false),
                    outcome_index = table.Column<int>(type: "integer", nullable: false),
                    token_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: true),
                    is_winner = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resolution_observation_outcomes", x => new { x.observation_id, x.outcome_index });
                    table.CheckConstraint("ck_resolution_observation_outcomes_index_nonnegative", "outcome_index >= 0");
                    table.ForeignKey(
                        name: "FK_resolution_observation_outcomes_resolution_observations_obs~",
                        column: x => x.observation_id,
                        principalSchema: "data_collection",
                        principalTable: "resolution_observations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resolution_states",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_scanned_raw_message_id = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    last_polling_cycle_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    primary_observation_id = table.Column<long>(type: "bigint", nullable: true),
                    confirming_observation_id = table.Column<long>(type: "bigint", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resolution_states", x => x.session_id);
                    table.CheckConstraint("ck_resolution_states_cursor_nonnegative", "last_scanned_raw_message_id >= 0");
                    table.ForeignKey(
                        name: "FK_resolution_states_collector_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resolution_states_resolution_observations_confirming_observ~",
                        column: x => x.confirming_observation_id,
                        principalSchema: "data_collection",
                        principalTable: "resolution_observations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resolution_states_resolution_observations_primary_observati~",
                        column: x => x.primary_observation_id,
                        principalSchema: "data_collection",
                        principalTable: "resolution_observations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resolution_observations_session_observed_id",
                schema: "data_collection",
                table: "resolution_observations",
                columns: new[] { "session_id", "observed_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_resolution_observations_ws_raw_item",
                schema: "data_collection",
                table: "resolution_observations",
                columns: new[] { "raw_message_id", "raw_item_index" },
                unique: true,
                filter: "raw_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_resolution_states_confirming_observation_id",
                schema: "data_collection",
                table: "resolution_states",
                column: "confirming_observation_id");

            migrationBuilder.CreateIndex(
                name: "IX_resolution_states_primary_observation_id",
                schema: "data_collection",
                table: "resolution_states",
                column: "primary_observation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resolution_observation_outcomes",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "resolution_states",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "resolution_observations",
                schema: "data_collection");

            migrationBuilder.DropColumn(
                name: "resolution_confirmed_at",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "resolution_connection_epoch",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "resolution_signaled_at",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "winning_outcome",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "winning_token_id",
                schema: "data_collection",
                table: "collector_sessions");
        }
    }
}
