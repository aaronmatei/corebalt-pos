using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hq_branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hq_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dispatched_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    is_received = table.Column<bool>(type: "boolean", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "received_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_store_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    dispatched_by = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatched_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfer_lines", x => new { x.stock_transfer_id, x.id });
                    table.ForeignKey(
                        name: "FK_stock_transfer_lines_stock_transfers_stock_transfer_id",
                        column: x => x.stock_transfer_id,
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hq_branches_tenant_store",
                table: "hq_branches",
                columns: new[] { "tenant_id", "store_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hq_transfers_tenant_to_received",
                table: "hq_transfers",
                columns: new[] { "tenant_id", "to_store_id", "is_received" });

            migrationBuilder.CreateIndex(
                name: "ux_received_transfers",
                table: "received_transfers",
                columns: new[] { "tenant_id", "store_id", "transfer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_store",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "store_id", "dispatched_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hq_branches");

            migrationBuilder.DropTable(
                name: "hq_transfers");

            migrationBuilder.DropTable(
                name: "received_transfers");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "stock_transfers");
        }
    }
}
