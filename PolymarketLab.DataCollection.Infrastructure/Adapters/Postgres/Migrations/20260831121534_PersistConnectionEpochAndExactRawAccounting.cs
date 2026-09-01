using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistConnectionEpochAndExactRawAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM data_collection.raw_market_messages)
                    THEN
                        RAISE EXCEPTION 'Cannot add connection epoch: raw_market_messages is not empty.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<long>(
                name: "connection_epoch",
                schema: "data_collection",
                table: "raw_market_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "current_connection_epoch",
                schema: "data_collection",
                table: "collector_session_progress",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "messages_enqueued",
                schema: "data_collection",
                table: "collector_session_progress",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_raw_market_messages_connection_epoch_positive",
                schema: "data_collection",
                table: "raw_market_messages",
                sql: "connection_epoch > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collector_session_progress_counters_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress",
                sql: "messages_received >= 0 AND messages_enqueued >= 0 AND messages_persisted >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collector_session_progress_epoch_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress",
                sql: "current_connection_epoch >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collector_session_progress_reconnect_count_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress",
                sql: "reconnect_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_raw_market_messages_connection_epoch_positive",
                schema: "data_collection",
                table: "raw_market_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_session_progress_counters_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_session_progress_epoch_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_session_progress_reconnect_count_nonnegative",
                schema: "data_collection",
                table: "collector_session_progress");

            migrationBuilder.DropColumn(
                name: "connection_epoch",
                schema: "data_collection",
                table: "raw_market_messages");

            migrationBuilder.DropColumn(
                name: "current_connection_epoch",
                schema: "data_collection",
                table: "collector_session_progress");

            migrationBuilder.DropColumn(
                name: "messages_enqueued",
                schema: "data_collection",
                table: "collector_session_progress");
        }
    }
}
