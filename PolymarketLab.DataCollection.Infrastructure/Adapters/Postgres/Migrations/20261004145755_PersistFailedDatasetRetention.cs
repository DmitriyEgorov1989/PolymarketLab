using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistFailedDatasetRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dataset_disposition",
                schema: "data_collection",
                table: "collector_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "failure_policy",
                schema: "data_collection",
                table: "collector_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "failure_retention_duration",
                schema: "data_collection",
                table: "collector_sessions",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retain_until",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retained_at",
                schema: "data_collection",
                table: "collector_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM data_collection.collector_sessions AS session
                        WHERE session.status = 4
                          AND NOT EXISTS (
                              SELECT 1
                              FROM data_collection.collector_dataset_cleanup_audits AS audit
                              WHERE audit.session_id = session.id)
                          AND EXISTS (
                              SELECT 1
                              FROM data_collection.raw_market_messages AS raw
                              WHERE raw.session_id = session.id))
                    THEN
                        RAISE EXCEPTION 'Cannot infer dataset disposition for legacy failed collector sessions with retained raw data.';
                    END IF;
                END $$;

                UPDATE data_collection.collector_sessions
                SET dataset_disposition = 1
                WHERE status = 4
                  AND (
                    EXISTS (
                      SELECT 1
                      FROM data_collection.collector_dataset_cleanup_audits AS audit
                      WHERE audit.session_id = collector_sessions.id)
                    OR NOT EXISTS (
                      SELECT 1
                      FROM data_collection.raw_market_messages AS raw
                      WHERE raw.session_id = collector_sessions.id))
                """);

            migrationBuilder.CreateIndex(
                name: "ix_collector_sessions_retained_expiry",
                schema: "data_collection",
                table: "collector_sessions",
                column: "retain_until",
                filter: "\"status\" = 4 AND \"dataset_disposition\" = 0 AND \"retain_until\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collector_sessions_retained_expiry",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "dataset_disposition",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "failure_policy",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "failure_retention_duration",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "retain_until",
                schema: "data_collection",
                table: "collector_sessions");

            migrationBuilder.DropColumn(
                name: "retained_at",
                schema: "data_collection",
                table: "collector_sessions");
        }
    }
}
