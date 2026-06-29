using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "received_transfers");

            migrationBuilder.CreateTable(
                name: "incoming_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_store_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dispatched_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    note = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    received_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incoming_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incoming_transfer_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incoming_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incoming_transfer_lines", x => new { x.incoming_transfer_id, x.id });
                    table.ForeignKey(
                        name: "FK_incoming_transfer_lines_incoming_transfers_incoming_transfe~",
                        column: x => x.incoming_transfer_id,
                        principalTable: "incoming_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_transfers_tenant_store_status",
                table: "incoming_transfers",
                columns: new[] { "tenant_id", "store_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incoming_transfer_lines");

            migrationBuilder.DropTable(
                name: "incoming_transfers");

            migrationBuilder.CreateTable(
                name: "received_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_received_transfers",
                table: "received_transfers",
                columns: new[] { "tenant_id", "store_id", "transfer_id" },
                unique: true);
        }
    }
}
