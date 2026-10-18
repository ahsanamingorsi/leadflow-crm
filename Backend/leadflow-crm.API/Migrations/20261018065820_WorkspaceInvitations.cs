using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedByWorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceInvitations", x => x.Id);
                    table.CheckConstraint("CK_WorkspaceInvitations_InviterWorkspace", "[InvitedByWorkspaceId] = [WorkspaceId]");
                    table.ForeignKey(
                        name: "FK_WorkspaceInvitations_AspNetRoles_RoleId_WorkspaceId",
                        columns: x => new { x.RoleId, x.WorkspaceId },
                        principalTable: "AspNetRoles",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceInvitations_AspNetUsers_InvitedByUserId_InvitedByWorkspaceId",
                        columns: x => new { x.InvitedByUserId, x.InvitedByWorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceInvitations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_InvitedByUserId_InvitedByWorkspaceId",
                table: "WorkspaceInvitations",
                columns: new[] { "InvitedByUserId", "InvitedByWorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_RoleId_WorkspaceId",
                table: "WorkspaceInvitations",
                columns: new[] { "RoleId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_TokenHash",
                table: "WorkspaceInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceInvitations_WorkspaceId_Email_AcceptedAtUtc_RevokedAtUtc",
                table: "WorkspaceInvitations",
                columns: new[] { "WorkspaceId", "Email", "AcceptedAtUtc", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceInvitations");
        }
    }
}
