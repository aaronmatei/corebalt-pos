using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancyMerchantSettingsEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition = table.Column<int>(type: "integer", nullable: false),
                    features = table.Column<int>(type: "integer", nullable: false),
                    max_tills = table.Column<int>(type: "integer", nullable: false),
                    max_branches = table.Column<int>(type: "integer", nullable: false),
                    license_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "etims_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    device_serial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    branch_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cmc_key = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etims_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trading_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kra_pin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    vat_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    receipt_footer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    show_powered_by = table.Column<bool>(type: "boolean", nullable: false),
                    setup_complete = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mpesa_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    short_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    consumer_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    consumer_secret = table.Column<string>(type: "text", nullable: false),
                    passkey = table.Column<string>(type: "text", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mpesa_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => new { x.merchant_profile_id, x.id });
                    table.ForeignKey(
                        name: "FK_branches_merchant_profiles_merchant_profile_id",
                        column: x => x.merchant_profile_id,
                        principalTable: "merchant_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_entitlements_tenant",
                table: "entitlements",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_etims_settings_tenant",
                table: "etims_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_merchant_profiles_tenant",
                table: "merchant_profiles",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_mpesa_settings_tenant",
                table: "mpesa_settings",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "entitlements");

            migrationBuilder.DropTable(
                name: "etims_settings");

            migrationBuilder.DropTable(
                name: "mpesa_settings");

            migrationBuilder.DropTable(
                name: "merchant_profiles");
        }
    }
}
