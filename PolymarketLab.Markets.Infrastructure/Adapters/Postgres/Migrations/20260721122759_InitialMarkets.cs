using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialMarkets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "markets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_market_id = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    condition_id = table.Column<string>(type: "text", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_markets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_token_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    outcome_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_market_tokens_markets_market_id",
                        column: x => x.market_id,
                        principalTable: "markets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_market_tokens_market_id_external_token_id",
                table: "market_tokens",
                columns: new[] { "market_id", "external_token_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_market_tokens_market_id_outcome_index",
                table: "market_tokens",
                columns: new[] { "market_id", "outcome_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_markets_condition_id",
                table: "markets",
                column: "condition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_markets_external_id",
                table: "markets",
                column: "external_market_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_markets_slug",
                table: "markets",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_tokens");

            migrationBuilder.DropTable(
                name: "markets");
        }
    }
}
