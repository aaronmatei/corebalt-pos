using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEtimsFiscalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "etims_signed_at_utc",
                table: "sales",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_status",
                table: "sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_sync_attempts",
                table: "sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "etims_signed_at_utc",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_status",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "fiscal_sync_attempts",
                table: "sales");
        }
    }
}
