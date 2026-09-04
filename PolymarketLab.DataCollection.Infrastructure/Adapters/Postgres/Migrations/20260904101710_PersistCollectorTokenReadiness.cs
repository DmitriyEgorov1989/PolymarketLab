using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistCollectorTokenReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collector_token_readiness",
                schema: "data_collection",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_epoch = table.Column<long>(type: "bigint", nullable: false),
                    token_id = table.Column<string>(type: "text", nullable: false),
                    initial_book_enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collector_token_readiness", x => new { x.session_id, x.connection_epoch, x.token_id });
                    table.CheckConstraint("ck_collector_token_readiness_epoch_positive", "connection_epoch > 0");
                    table.ForeignKey(
                        name: "FK_collector_token_readiness_collector_sessions_session_id",
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
                name: "collector_token_readiness",
                schema: "data_collection");
        }
    }
}
