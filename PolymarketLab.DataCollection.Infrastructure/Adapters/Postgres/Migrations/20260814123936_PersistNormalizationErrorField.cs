using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PersistNormalizationErrorField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_field",
                schema: "data_collection",
                table: "raw_message_normalizations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error_field",
                schema: "data_collection",
                table: "raw_message_normalizations");
        }
    }
}
