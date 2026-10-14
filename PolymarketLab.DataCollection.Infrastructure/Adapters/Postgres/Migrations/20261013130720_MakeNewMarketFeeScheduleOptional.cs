using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class MakeNewMarketFeeScheduleOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "fee_schedule_taker_only",
                schema: "data_collection",
                table: "new_markets",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_rebate_rate",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_rate",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_exponent",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "fee_schedule_taker_only",
                schema: "data_collection",
                table: "new_markets",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_rebate_rate",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_rate",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "fee_schedule_exponent",
                schema: "data_collection",
                table: "new_markets",
                type: "numeric(29,18)",
                precision: 29,
                scale: 18,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(29,18)",
                oldPrecision: 29,
                oldScale: 18,
                oldNullable: true);
        }
    }
}
