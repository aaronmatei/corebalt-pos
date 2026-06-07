using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFingerprintCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_fingerprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template = table.Column<string>(type: "text", nullable: false),
                    finger_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    enrolled_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consent_given = table.Column<bool>(type: "boolean", nullable: false),
                    consent_recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_fingerprints", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_fingerprints_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_fingerprints_user",
                table: "user_fingerprints",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_fingerprints");
        }
    }
}
