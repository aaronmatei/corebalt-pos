using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHqSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hq_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opened_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    closed_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    counted_cash = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    variance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    variance_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    synced_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hq_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hq_sessions_tenant_store_closed",
                table: "hq_sessions",
                columns: new[] { "tenant_id", "store_id", "closed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hq_sessions");
        }
    }
}
