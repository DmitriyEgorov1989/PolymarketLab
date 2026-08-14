using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizationReplayIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_raw_market_messages_session_id",
                schema: "data_collection",
                table: "raw_market_messages",
                columns: new[] { "session_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_normalized_events_projection_event_raw_message",
                schema: "data_collection",
                table: "normalized_events",
                columns: new[] { "projection_version", "event_type", "raw_message_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_raw_market_messages_session_id",
                schema: "data_collection",
                table: "raw_market_messages");

            migrationBuilder.DropIndex(
                name: "ix_normalized_events_projection_event_raw_message",
                schema: "data_collection",
                table: "normalized_events");
        }
    }
}
