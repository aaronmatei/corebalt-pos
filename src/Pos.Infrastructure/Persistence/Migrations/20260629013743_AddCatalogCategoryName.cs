using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogCategoryName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_name",
                table: "catalog_items",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category_name",
                table: "catalog_changes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_name",
                table: "catalog_items");

            migrationBuilder.DropColumn(
                name: "category_name",
                table: "catalog_changes");
        }
    }
}
