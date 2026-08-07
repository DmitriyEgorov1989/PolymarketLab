using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectorSessionProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collector_session_progress",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    messages_received = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    messages_persisted = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reconnect_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collector_session_progress", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_collector_session_progress_collector_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO data_collection.collector_session_progress (
                    session_id,
                    messages_received,
                    messages_persisted,
                    last_message_at,
                    reconnect_count)
                SELECT
                    sessions.id,
                    COUNT(messages.id),
                    COUNT(messages.id),
                    MAX(messages.received_at),
                    0
                FROM data_collection.collector_sessions AS sessions
                LEFT JOIN data_collection.raw_market_messages AS messages
                    ON messages.session_id = sessions.id
                GROUP BY sessions.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collector_session_progress",
                schema: "data_collection");
        }
    }
}
