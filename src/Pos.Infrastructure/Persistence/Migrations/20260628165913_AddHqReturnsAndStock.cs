using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHqReturnsAndStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hq_credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    original_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_void = table.Column<bool>(type: "boolean", nullable: false),
                    authorized_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    refund_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    refund_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_credit_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hq_stock_on_hand",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    on_hand = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    last_movement_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_stock_on_hand", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hq_credit_notes_tenant_store_created",
                table: "hq_credit_notes",
                columns: new[] { "tenant_id", "store_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_hq_stock_tenant_store_product",
                table: "hq_stock_on_hand",
                columns: new[] { "tenant_id", "store_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hq_credit_notes");

            migrationBuilder.DropTable(
                name: "hq_stock_on_hand");
        }
    }
}
