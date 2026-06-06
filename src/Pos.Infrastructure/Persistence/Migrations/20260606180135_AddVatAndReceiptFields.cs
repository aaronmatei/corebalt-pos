using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVatAndReceiptFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "etims_cuin",
                table: "sales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "etims_qr_url",
                table: "sales",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "etims_signature",
                table: "sales",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "etims_transmitted_at_utc",
                table: "sales",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "grand_total_amount",
                table: "sales",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "grand_total_currency",
                table: "sales",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "KES");

            migrationBuilder.AddColumn<int>(
                name: "tax_class",
                table: "sale_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_amount",
                table: "sale_lines",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "taxable_currency",
                table: "sale_lines",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "KES");

            migrationBuilder.AddColumn<int>(
                name: "unit_of_measure",
                table: "sale_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_amount",
                table: "sale_lines",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "vat_currency",
                table: "sale_lines",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "KES");

            migrationBuilder.AddColumn<int>(
                name: "tax_class",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "sale_vat_summary",
                columns: table => new
                {
                    tax_class = table.Column<int>(type: "integer", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    taxable_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vat_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_vat_summary", x => new { x.sale_id, x.tax_class });
                    table.ForeignKey(
                        name: "FK_sale_vat_summary_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_vat_summary");

            migrationBuilder.DropColumn(
                name: "etims_cuin",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "etims_qr_url",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "etims_signature",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "etims_transmitted_at_utc",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "grand_total_amount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "grand_total_currency",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "tax_class",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "taxable_amount",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "taxable_currency",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "unit_of_measure",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "vat_amount",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "vat_currency",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "tax_class",
                table: "products");
        }
    }
}
