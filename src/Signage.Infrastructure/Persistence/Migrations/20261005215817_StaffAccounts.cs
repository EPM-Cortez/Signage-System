using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentitySubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExternalSubject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LoginName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedLoginName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSignInUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccounts_IdentitySubject",
                table: "StaffAccounts",
                column: "IdentitySubject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccounts_Provider_ExternalSubject",
                table: "StaffAccounts",
                columns: new[] { "Provider", "ExternalSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccounts_Provider_NormalizedLoginName",
                table: "StaffAccounts",
                columns: new[] { "Provider", "NormalizedLoginName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffAccounts");
        }
    }
}
