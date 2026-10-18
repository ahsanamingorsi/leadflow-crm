using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class PhaseSixSevenWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IntakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsOptOut = table.Column<bool>(type: "bit", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    ContentSid = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    ClientKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_CrmContacts_ContactId_WorkspaceId",
                        columns: x => new { x.ContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ContactId_WorkspaceId",
                table: "WhatsAppMessages",
                columns: new[] { "ContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_WorkspaceId_ClientKey",
                table: "WhatsAppMessages",
                columns: new[] { "WorkspaceId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_WorkspaceId_Phone_CreatedAtUtc",
                table: "WhatsAppMessages",
                columns: new[] { "WorkspaceId", "Phone", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_WorkspaceId_ProviderMessageId",
                table: "WhatsAppMessages",
                columns: new[] { "WorkspaceId", "ProviderMessageId" },
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessages");
        }
    }
}
