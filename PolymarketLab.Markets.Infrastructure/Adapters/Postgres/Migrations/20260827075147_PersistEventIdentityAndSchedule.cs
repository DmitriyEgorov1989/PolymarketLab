using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.Markets.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistEventIdentityAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM markets LIMIT 1) THEN
                        RAISE EXCEPTION 'PersistEventIdentityAndSchedule requires an empty markets table. Recreate the disposable database before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "starts_at",
                table: "markets");

            migrationBuilder.RenameColumn(
                name: "slug",
                table: "markets",
                newName: "market_slug");

            migrationBuilder.DropColumn(
                name: "ends_at",
                table: "markets");

            migrationBuilder.RenameIndex(
                name: "ux_markets_slug",
                table: "markets",
                newName: "ux_markets_market_slug");

            migrationBuilder.RenameIndex(
                name: "ux_markets_external_id",
                table: "markets",
                newName: "ux_markets_external_market_id");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discovered_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "event_ends_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "event_slug",
                table: "markets",
                type: "text",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "event_starts_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "external_closed_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "external_created_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_event_id",
                table: "markets",
                type: "text",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "gamma_start_date",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "orders_opened_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "schedule_refreshed_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ux_markets_event_slug",
                table: "markets",
                column: "event_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_markets_external_event_id",
                table: "markets",
                column: "external_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_market_tokens_external_token_id",
                table: "market_tokens",
                column: "external_token_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM markets LIMIT 1) THEN
                        RAISE EXCEPTION 'PersistEventIdentityAndSchedule rollback requires an empty markets table.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_markets_event_slug",
                table: "markets");

            migrationBuilder.DropIndex(
                name: "ux_markets_external_event_id",
                table: "markets");

            migrationBuilder.DropIndex(
                name: "ux_market_tokens_external_token_id",
                table: "market_tokens");

            migrationBuilder.DropColumn(
                name: "discovered_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "event_ends_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "event_slug",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "event_starts_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "external_closed_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "external_created_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "external_event_id",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "gamma_start_date",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "orders_opened_at",
                table: "markets");

            migrationBuilder.DropColumn(
                name: "schedule_refreshed_at",
                table: "markets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "starts_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "market_slug",
                table: "markets",
                newName: "slug");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ends_at",
                table: "markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.RenameIndex(
                name: "ux_markets_market_slug",
                table: "markets",
                newName: "ux_markets_slug");

            migrationBuilder.RenameIndex(
                name: "ux_markets_external_market_id",
                table: "markets",
                newName: "ux_markets_external_id");
        }
    }
}
