using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHqSyncReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sync_secret_hash",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hq_sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    register_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cashier_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_vat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    enqueued_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    projected_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_inbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hq_sales_tenant_store_completed",
                table: "hq_sales",
                columns: new[] { "tenant_id", "store_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_inbox_tenant_store",
                table: "sync_inbox",
                columns: new[] { "tenant_id", "store_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hq_sales");

            migrationBuilder.DropTable(
                name: "sync_inbox");

            migrationBuilder.DropColumn(
                name: "sync_secret_hash",
                table: "tenants");
        }
    }
}
