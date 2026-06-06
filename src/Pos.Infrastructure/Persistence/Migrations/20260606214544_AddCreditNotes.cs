using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_etims_cuin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    authorized_by = table.Column<Guid>(type: "uuid", nullable: false),
                    authorized_by_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    authorized_by_staff_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    refund_method = table.Column<int>(type: "integer", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    refund_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    refund_status = table.Column<int>(type: "integer", nullable: false),
                    return_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    grand_total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    fiscal_status = table.Column<int>(type: "integer", nullable: false),
                    etims_cuin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    etims_signature = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    etims_qr_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    etims_signed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_class = table.Column<int>(type: "integer", nullable: false),
                    unit_of_measure = table.Column<int>(type: "integer", nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    taxable_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_note_lines", x => new { x.credit_note_id, x.id });
                    table.ForeignKey(
                        name: "FK_credit_note_lines_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_store_sale",
                table: "credit_notes",
                columns: new[] { "tenant_id", "store_id", "original_sale_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_note_lines");

            migrationBuilder.DropTable(
                name: "credit_notes");
        }
    }
}
