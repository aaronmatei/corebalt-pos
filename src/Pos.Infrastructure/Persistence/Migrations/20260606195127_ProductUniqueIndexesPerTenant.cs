using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductUniqueIndexesPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_tenant_barcode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ux_products_tenant_store_sku",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ux_products_tenant_barcode",
                table: "products",
                columns: new[] { "tenant_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_products_tenant_sku",
                table: "products",
                columns: new[] { "tenant_id", "sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_products_tenant_barcode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ux_products_tenant_sku",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_barcode",
                table: "products",
                columns: new[] { "tenant_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "ux_products_tenant_store_sku",
                table: "products",
                columns: new[] { "tenant_id", "store_id", "sku" },
                unique: true);
        }
    }
}
