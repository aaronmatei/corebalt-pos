using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashManagementSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "register_session_id",
                table: "sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "is_void",
                table: "credit_notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "register_session_id",
                table: "credit_notes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "register_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    opening_float_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    opening_float_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    counted_cash_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    counted_cash_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    expected_cash_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    expected_cash_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    variance_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    variance_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_register_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_store_completed",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_store_session",
                table: "sales",
                columns: new[] { "tenant_id", "store_id", "register_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_store_created",
                table: "credit_notes",
                columns: new[] { "tenant_id", "store_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_store_session",
                table: "credit_notes",
                columns: new[] { "tenant_id", "store_id", "register_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_tenant_store_session",
                table: "cash_movements",
                columns: new[] { "tenant_id", "store_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_register_sessions_tenant_store_opened",
                table: "register_sessions",
                columns: new[] { "tenant_id", "store_id", "opened_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_register_sessions_open_per_register",
                table: "register_sessions",
                columns: new[] { "tenant_id", "store_id", "register_id" },
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropTable(
                name: "register_sessions");

            migrationBuilder.DropIndex(
                name: "ix_sales_tenant_store_completed",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sales_tenant_store_session",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_credit_notes_tenant_store_created",
                table: "credit_notes");

            migrationBuilder.DropIndex(
                name: "ix_credit_notes_tenant_store_session",
                table: "credit_notes");

            migrationBuilder.DropColumn(
                name: "register_session_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "is_void",
                table: "credit_notes");

            migrationBuilder.DropColumn(
                name: "register_session_id",
                table: "credit_notes");
        }
    }
}
