using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LibraryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Presentations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArchivedUtc",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PendingContentDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsPackage = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingContentDeletions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PresentationFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresentationFolders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Presentations_FolderId",
                table: "Presentations",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_PresentationFolders_NormalizedName",
                table: "PresentationFolders",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Presentations_PresentationFolders_FolderId",
                table: "Presentations",
                column: "FolderId",
                principalTable: "PresentationFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Presentations_PresentationFolders_FolderId",
                table: "Presentations");

            migrationBuilder.DropTable(
                name: "PendingContentDeletions");

            migrationBuilder.DropTable(
                name: "PresentationFolders");

            migrationBuilder.DropIndex(
                name: "IX_Presentations_FolderId",
                table: "Presentations");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Presentations");

            migrationBuilder.DropColumn(
                name: "ArchivedUtc",
                table: "Devices");
        }
    }
}
