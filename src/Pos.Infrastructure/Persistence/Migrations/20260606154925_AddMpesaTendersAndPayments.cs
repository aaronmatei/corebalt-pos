using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMpesaTendersAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_reference",
                table: "tenders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Backfill existing tenders as Confirmed (1): every pre-existing tender belongs to an
            // already-completed cash sale. New tenders get their real status from the entity on insert.
            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "tenders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "mpesa_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    merchant_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    msisdn_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    result_code = table.Column<int>(type: "integer", nullable: true),
                    result_description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    mpesa_receipt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mpesa_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mpesa_tenant_store_sale",
                table: "mpesa_payments",
                columns: new[] { "tenant_id", "store_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ux_mpesa_checkout_request_id",
                table: "mpesa_payments",
                column: "checkout_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mpesa_payments");

            migrationBuilder.DropColumn(
                name: "provider_reference",
                table: "tenders");

            migrationBuilder.DropColumn(
                name: "status",
                table: "tenders");
        }
    }
}
