using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistCollectorDatasetCleanupAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collector_dataset_cleanup_audits",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_raw_message_count = table.Column<long>(type: "bigint", nullable: false),
                    deleted_normalization_count = table.Column<long>(type: "bigint", nullable: false),
                    deleted_normalized_event_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collector_dataset_cleanup_audits", x => x.session_id);
                    table.CheckConstraint("ck_collector_dataset_cleanup_audits_counts_nonnegative", "deleted_raw_message_count >= 0 AND deleted_normalization_count >= 0 AND deleted_normalized_event_count >= 0");
                    table.ForeignKey(
                        name: "FK_collector_dataset_cleanup_audits_collector_sessions_session~",
                        column: x => x.session_id,
                        principalSchema: "data_collection",
                        principalTable: "collector_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collector_dataset_cleanup_audits",
                schema: "data_collection");
        }
    }
}
