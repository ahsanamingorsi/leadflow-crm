using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFiveDashboardSavedLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsShared = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedLists_AspNetUsers_OwnerUserId_WorkspaceId",
                        columns: x => new { x.OwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavedLists_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedLists_OwnerUserId_WorkspaceId",
                table: "SavedLists",
                columns: new[] { "OwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedLists_WorkspaceId_EntityType_IsShared_UpdatedAtUtc",
                table: "SavedLists",
                columns: new[] { "WorkspaceId", "EntityType", "IsShared", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedLists_WorkspaceId_OwnerUserId_Name",
                table: "SavedLists",
                columns: new[] { "WorkspaceId", "OwnerUserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedLists");
        }
    }
}
