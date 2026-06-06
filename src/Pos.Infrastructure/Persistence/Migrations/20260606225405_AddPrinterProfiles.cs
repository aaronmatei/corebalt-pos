using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "printer_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport = table.Column<int>(type: "integer", nullable: false),
                    network_host = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    network_port = table.Column<int>(type: "integer", nullable: false),
                    file_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    paper_width = table.Column<int>(type: "integer", nullable: false),
                    has_cutter = table.Column<bool>(type: "boolean", nullable: false),
                    has_cash_drawer = table.Column<bool>(type: "boolean", nullable: false),
                    native_qr_supported = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printer_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_printer_profiles_tenant_register",
                table: "printer_profiles",
                columns: new[] { "tenant_id", "register_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "printer_profiles");
        }
    }
}
