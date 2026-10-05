using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Presentations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedBySubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchivedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Presentations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreenGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PresentationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PresentationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    SourceStorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SlideCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ManifestStorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FailureDetail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadyUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresentationVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PresentationVersions_Presentations_PresentationId",
                        column: x => x.PresentationId,
                        principalTable: "Presentations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ScreenGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PairedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastSeenUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastIpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    BrowserSummary = table.Column<string>(type: "TEXT", nullable: true),
                    PlayingContentId = table.Column<string>(type: "TEXT", nullable: true),
                    PlayingPresentationVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_ScreenGroups_ScreenGroupId",
                        column: x => x.ScreenGroupId,
                        principalTable: "ScreenGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PublisherAccess",
                columns: table => new
                {
                    IdentitySubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ScreenGroupId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublisherAccess", x => new { x.IdentitySubject, x.ScreenGroupId });
                    table.ForeignKey(
                        name: "FK_PublisherAccess_ScreenGroups_ScreenGroupId",
                        column: x => x.ScreenGroupId,
                        principalTable: "ScreenGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PresentationVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseExpiresUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversionJobs_PresentationVersions_PresentationVersionId",
                        column: x => x.PresentationVersionId,
                        principalTable: "PresentationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScreenGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PresentationVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartsUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndsUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedBySubject = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publications_PresentationVersions_PresentationVersionId",
                        column: x => x.PresentationVersionId,
                        principalTable: "PresentationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Publications_ScreenGroups_ScreenGroupId",
                        column: x => x.ScreenGroupId,
                        principalTable: "ScreenGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublishTargets",
                columns: table => new
                {
                    PresentationVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScreenGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartsUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndsUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishWhenReady = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishTargets", x => new { x.PresentationVersionId, x.ScreenGroupId });
                    table.ForeignKey(
                        name: "FK_PublishTargets_PresentationVersions_PresentationVersionId",
                        column: x => x.PresentationVersionId,
                        principalTable: "PresentationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PublishTargets_ScreenGroups_ScreenGroupId",
                        column: x => x.ScreenGroupId,
                        principalTable: "ScreenGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PairingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TemporaryTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ApprovedDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProtectedDeviceToken = table.Column<string>(type: "TEXT", nullable: true),
                    ConsumedUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PairingSessions_Devices_ApprovedDeviceId",
                        column: x => x.ApprovedDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredUtc",
                table: "AuditEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_PresentationVersionId",
                table: "ConversionJobs",
                column: "PresentationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_Status_AvailableUtc",
                table: "ConversionJobs",
                columns: new[] { "Status", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ScreenGroupId",
                table: "Devices",
                column: "ScreenGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TokenHash",
                table: "Devices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_ApprovedDeviceId",
                table: "PairingSessions",
                column: "ApprovedDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_CodeHash",
                table: "PairingSessions",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_PairingSessions_TemporaryTokenHash",
                table: "PairingSessions",
                column: "TemporaryTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PresentationVersions_PresentationId_VersionNumber",
                table: "PresentationVersions",
                columns: new[] { "PresentationId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Publications_PresentationVersionId",
                table: "Publications",
                column: "PresentationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_ScreenGroupId_IsEnabled_StartsUtc",
                table: "Publications",
                columns: new[] { "ScreenGroupId", "IsEnabled", "StartsUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublisherAccess_ScreenGroupId",
                table: "PublisherAccess",
                column: "ScreenGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishTargets_ScreenGroupId",
                table: "PublishTargets",
                column: "ScreenGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreenGroups_Name",
                table: "ScreenGroups",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ConversionJobs");

            migrationBuilder.DropTable(
                name: "PairingSessions");

            migrationBuilder.DropTable(
                name: "Publications");

            migrationBuilder.DropTable(
                name: "PublisherAccess");

            migrationBuilder.DropTable(
                name: "PublishTargets");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "PresentationVersions");

            migrationBuilder.DropTable(
                name: "ScreenGroups");

            migrationBuilder.DropTable(
                name: "Presentations");
        }
    }
}
