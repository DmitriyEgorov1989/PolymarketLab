using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistNormalizationSessionCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "normalization_session_cursors",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false),
                    last_claimed_raw_message_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_normalization_session_cursors", x => new { x.session_id, x.projection_version });
                    table.CheckConstraint("ck_normalization_session_cursors_projection_version_positive", "projection_version > 0");
                    table.CheckConstraint("ck_normalization_session_cursors_raw_message_id_positive", "last_claimed_raw_message_id > 0");
                    table.ForeignKey(
                        name: "FK_normalization_session_cursors_collector_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO data_collection.normalization_session_cursors
                    (session_id, projection_version, last_claimed_raw_message_id)
                SELECT
                    session.id,
                    session.projection_version,
                    MAX(normalization.raw_message_id)
                FROM data_collection.collector_sessions AS session
                INNER JOIN data_collection.raw_market_messages AS raw
                    ON raw.session_id = session.id
                INNER JOIN data_collection.raw_message_normalizations AS normalization
                    ON normalization.raw_message_id = raw.id
                   AND normalization.projection_version = session.projection_version
                WHERE session.invalidating_at IS NULL
                  AND session.status IN (0, 1, 2)
                  AND session.projection_version IS NOT NULL
                GROUP BY session.id, session.projection_version
                ON CONFLICT (session_id, projection_version)
                DO UPDATE SET
                    last_claimed_raw_message_id = GREATEST(
                        normalization_session_cursors.last_claimed_raw_message_id,
                        EXCLUDED.last_claimed_raw_message_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "normalization_session_cursors",
                schema: "data_collection");
        }
    }
}
