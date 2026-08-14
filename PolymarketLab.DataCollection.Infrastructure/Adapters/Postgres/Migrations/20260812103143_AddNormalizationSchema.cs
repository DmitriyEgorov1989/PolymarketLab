using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "normalized_events",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    raw_item_index = table.Column<int>(type: "integer", nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false),
                    normalizer_version = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_timestamp = table.Column<long>(type: "bigint", nullable: true),
                    market_condition_id = table.Column<string>(type: "text", nullable: true),
                    asset_id = table.Column<string>(type: "text", nullable: true),
                    normalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_normalized_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_normalized_events_raw_market_messages_raw_message_id",
                        column: x => x.raw_message_id,
                        principalSchema: "data_collection",
                        principalTable: "raw_market_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raw_message_normalizations",
                schema: "data_collection",
                columns: table => new
                {
                    raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_message_normalizations", x => new { x.raw_message_id, x.projection_version });
                    table.ForeignKey(
                        name: "FK_raw_message_normalizations_raw_market_messages_raw_message_~",
                        column: x => x.raw_message_id,
                        principalSchema: "data_collection",
                        principalTable: "raw_market_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "best_bid_asks",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    best_bid = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    best_ask = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    spread = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_best_bid_asks", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_best_bid_asks_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_snapshots",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    tick_size = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: true),
                    last_trade_price = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_snapshots", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_book_snapshots_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "last_trade_price",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    size = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: true),
                    side = table.Column<int>(type: "integer", nullable: false),
                    fee_rate_bps = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: true),
                    transaction_hash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_last_trade_price", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_last_trade_price_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_resolutions",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    external_market_id = table.Column<string>(type: "text", nullable: false),
                    winning_asset_id = table.Column<string>(type: "text", nullable: false),
                    winning_outcome = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_resolutions", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_market_resolutions_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "new_markets",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    external_market_id = table.Column<string>(type: "text", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    sports_market_type = table.Column<string>(type: "text", nullable: false),
                    line = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: true),
                    game_start_time = table.Column<string>(type: "text", nullable: false),
                    order_price_min_tick_size = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    group_item_title = table.Column<string>(type: "text", nullable: false),
                    taker_base_fee = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false),
                    fees_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    event_message_id = table.Column<string>(type: "text", nullable: false),
                    event_message_ticker = table.Column<string>(type: "text", nullable: false),
                    event_message_slug = table.Column<string>(type: "text", nullable: false),
                    event_message_title = table.Column<string>(type: "text", nullable: false),
                    event_message_description = table.Column<string>(type: "text", nullable: false),
                    fee_schedule_exponent = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false),
                    fee_schedule_rate = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false),
                    fee_schedule_rebate_rate = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false),
                    fee_schedule_taker_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_new_markets", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_new_markets_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_change",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    item_index = table.Column<int>(type: "integer", nullable: false),
                    asset_id = table.Column<string>(type: "text", nullable: false),
                    source_timestamp = table.Column<long>(type: "bigint", nullable: true),
                    price = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    size = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: true),
                    best_bid = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: true),
                    best_ask = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_change", x => x.id);
                    table.ForeignKey(
                        name: "FK_price_change_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tick_size_changes",
                schema: "data_collection",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    old_tick_size = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    new_tick_size = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tick_size_changes", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_tick_size_changes_normalized_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "normalized_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_levels",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    side = table.Column<int>(type: "integer", nullable: false),
                    level_index = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(29,28)", precision: 29, scale: 28, nullable: false),
                    size = table.Column<decimal>(type: "numeric(29,18)", precision: 29, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_levels", x => x.id);
                    table.ForeignKey(
                        name: "FK_book_levels_book_snapshots_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "book_snapshots",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_resolution_assets",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    item_index = table.Column<int>(type: "integer", nullable: false),
                    asset_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_resolution_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_market_resolution_assets_market_resolutions_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "market_resolutions",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "new_market_assets",
                schema: "data_collection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    item_index = table.Column<int>(type: "integer", nullable: false),
                    asset_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_new_market_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_new_market_assets_new_markets_event_id",
                        column: x => x.event_id,
                        principalSchema: "data_collection",
                        principalTable: "new_markets",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_book_levels_event_side_level_index",
                schema: "data_collection",
                table: "book_levels",
                columns: new[] { "event_id", "side", "level_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_market_resolution_assets_event_id_item_index",
                schema: "data_collection",
                table: "market_resolution_assets",
                columns: new[] { "event_id", "item_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_new_market_assets_event_id_item_index",
                schema: "data_collection",
                table: "new_market_assets",
                columns: new[] { "event_id", "item_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_normalized_events_raw_message_item_projection",
                schema: "data_collection",
                table: "normalized_events",
                columns: new[] { "raw_message_id", "raw_item_index", "projection_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_change_asset_id_source_timestamp",
                schema: "data_collection",
                table: "price_change",
                columns: new[] { "asset_id", "source_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ux_price_change_event_id_item_index",
                schema: "data_collection",
                table: "price_change",
                columns: new[] { "event_id", "item_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_message_normalizations_projection_status_raw_message_id",
                schema: "data_collection",
                table: "raw_message_normalizations",
                columns: new[] { "projection_version", "status", "raw_message_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "best_bid_asks",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "book_levels",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "last_trade_price",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "market_resolution_assets",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "new_market_assets",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "price_change",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "raw_message_normalizations",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "tick_size_changes",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "book_snapshots",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "market_resolutions",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "new_markets",
                schema: "data_collection");

            migrationBuilder.DropTable(
                name: "normalized_events",
                schema: "data_collection");
        }
    }
}
