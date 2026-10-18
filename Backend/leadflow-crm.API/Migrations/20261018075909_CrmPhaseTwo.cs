using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class CrmPhaseTwo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmActivities_AspNetUsers_ActorUserId_WorkspaceId",
                        columns: x => new { x.ActorUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmCompanies", x => x.Id);
                    table.UniqueConstraint("AK_CrmCompanies_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_CrmCompanies_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmPipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmPipelines", x => x.Id);
                    table.UniqueConstraint("AK_CrmPipelines_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_CrmPipelines_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptedOut = table.Column<bool>(type: "bit", nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmContacts", x => x.Id);
                    table.UniqueConstraint("AK_CrmContacts_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_CrmContacts_AspNetUsers_OwnerUserId_WorkspaceId",
                        columns: x => new { x.OwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmContacts_CrmCompanies_CompanyId_WorkspaceId",
                        columns: x => new { x.CompanyId, x.WorkspaceId },
                        principalTable: "CrmCompanies",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmContacts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmCustomFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PipelineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FieldType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmCustomFields", x => x.Id);
                    table.UniqueConstraint("AK_CrmCustomFields_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_CrmCustomFields_CrmPipelines_PipelineId_WorkspaceId",
                        columns: x => new { x.PipelineId, x.WorkspaceId },
                        principalTable: "CrmPipelines",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmCustomFields_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmPipelineStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmPipelineStages", x => x.Id);
                    table.UniqueConstraint("AK_CrmPipelineStages_Id_PipelineId_WorkspaceId", x => new { x.Id, x.PipelineId, x.WorkspaceId });
                    table.UniqueConstraint("AK_CrmPipelineStages_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_CrmPipelineStages_CrmPipelines_PipelineId_WorkspaceId",
                        columns: x => new { x.PipelineId, x.WorkspaceId },
                        principalTable: "CrmPipelines",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmPipelineStages_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmOpportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpectedCloseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmOpportunities", x => x.Id);
                    table.UniqueConstraint("AK_CrmOpportunities_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.CheckConstraint("CK_CrmOpportunities_Value", "[Value] >= 0 AND [CurrencyCode] = 'PKR'");
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_AspNetUsers_OwnerUserId_WorkspaceId",
                        columns: x => new { x.OwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmContacts_ContactId_WorkspaceId",
                        columns: x => new { x.ContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmPipelineStages_StageId_PipelineId_WorkspaceId",
                        columns: x => new { x.StageId, x.PipelineId, x.WorkspaceId },
                        principalTable: "CrmPipelineStages",
                        principalColumns: new[] { "Id", "PipelineId", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmStageRequiredFields",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmStageRequiredFields", x => new { x.WorkspaceId, x.StageId, x.FieldId });
                    table.ForeignKey(
                        name: "FK_CrmStageRequiredFields_CrmCustomFields_FieldId_WorkspaceId",
                        columns: x => new { x.FieldId, x.WorkspaceId },
                        principalTable: "CrmCustomFields",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmStageRequiredFields_CrmPipelineStages_StageId_WorkspaceId",
                        columns: x => new { x.StageId, x.WorkspaceId },
                        principalTable: "CrmPipelineStages",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_ActorUserId_WorkspaceId",
                table: "CrmActivities",
                columns: new[] { "ActorUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_WorkspaceId_EntityType_RecordId_OccurredAtUtc",
                table: "CrmActivities",
                columns: new[] { "WorkspaceId", "EntityType", "RecordId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCompanies_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmCompanies",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContacts_CompanyId_WorkspaceId",
                table: "CrmContacts",
                columns: new[] { "CompanyId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContacts_OwnerUserId_WorkspaceId",
                table: "CrmContacts",
                columns: new[] { "OwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContacts_WorkspaceId_Email",
                table: "CrmContacts",
                columns: new[] { "WorkspaceId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContacts_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmContacts",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmContacts_WorkspaceId_Phone",
                table: "CrmContacts",
                columns: new[] { "WorkspaceId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomFields_PipelineId_WorkspaceId",
                table: "CrmCustomFields",
                columns: new[] { "PipelineId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomFields_WorkspaceId_EntityType_PipelineId_Name",
                table: "CrmCustomFields",
                columns: new[] { "WorkspaceId", "EntityType", "PipelineId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustomFields_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmCustomFields",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_ContactId_WorkspaceId",
                table: "CrmOpportunities",
                columns: new[] { "ContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_OwnerUserId_WorkspaceId",
                table: "CrmOpportunities",
                columns: new[] { "OwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_StageId_PipelineId_WorkspaceId",
                table: "CrmOpportunities",
                columns: new[] { "StageId", "PipelineId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmOpportunities",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_WorkspaceId_OwnerUserId_StageId",
                table: "CrmOpportunities",
                columns: new[] { "WorkspaceId", "OwnerUserId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelines_WorkspaceId",
                table: "CrmPipelines",
                column: "WorkspaceId",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelines_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmPipelines",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelines_WorkspaceId_Name",
                table: "CrmPipelines",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_PipelineId_WorkspaceId",
                table: "CrmPipelineStages",
                columns: new[] { "PipelineId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_WorkspaceId_IsDeleted_CreatedAtUtc",
                table: "CrmPipelineStages",
                columns: new[] { "WorkspaceId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_WorkspaceId_PipelineId_Name",
                table: "CrmPipelineStages",
                columns: new[] { "WorkspaceId", "PipelineId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_WorkspaceId_PipelineId_Position",
                table: "CrmPipelineStages",
                columns: new[] { "WorkspaceId", "PipelineId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageRequiredFields_FieldId_WorkspaceId",
                table: "CrmStageRequiredFields",
                columns: new[] { "FieldId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageRequiredFields_StageId_WorkspaceId",
                table: "CrmStageRequiredFields",
                columns: new[] { "StageId", "WorkspaceId" });

            // Existing workspaces receive the same starting pipeline and permissions as new signups.
            migrationBuilder.Sql("""
                INSERT INTO CrmPipelines (Id, WorkspaceId, Name, IsDefault, IsDeleted, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), Id, N'Sales', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME() FROM Workspaces;

                INSERT INTO CrmPipelineStages (Id, WorkspaceId, PipelineId, Name, Position, Kind, IsDeleted, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), p.WorkspaceId, p.Id, s.Name, s.Position, s.Kind, 0, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM CrmPipelines p CROSS JOIN (VALUES
                  (N'New',0,N'Open'),(N'Qualified',1,N'Open'),(N'Proposal',2,N'Open'),(N'Won',3,N'Won'),(N'Lost',4,N'Lost')
                ) s(Name,Position,Kind);

                INSERT INTO RolePermissions (WorkspaceId, RoleId, PermissionKey, GrantedAtUtc)
                SELECT r.WorkspaceId, r.Id, p.PermissionKey, SYSUTCDATETIME()
                FROM AspNetRoles r CROSS JOIN (VALUES (N'crm.pipelines.manage'),(N'crm.custom-fields.manage')) p(PermissionKey)
                WHERE r.IsSystemRole = 1 AND r.DisplayName IN (N'Owner',N'Admin')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions existing WHERE existing.WorkspaceId = r.WorkspaceId
                    AND existing.RoleId = r.Id AND existing.PermissionKey = p.PermissionKey);

                INSERT INTO AuditLogs (Id, WorkspaceId, ActorType, EventType, TargetType, TargetId, OccurredAtUtc, AfterJson)
                SELECT NEWID(), WorkspaceId, N'System', N'crm.pipeline.initialized', N'Pipeline', CONVERT(nvarchar(120),Id),
                  SYSUTCDATETIME(), N'{"Name":"Sales","Stages":["New","Qualified","Proposal","Won","Lost"]}' FROM CrmPipelines;

                INSERT INTO AuditLogs (Id, WorkspaceId, ActorType, EventType, TargetType, TargetId, OccurredAtUtc, AfterJson)
                SELECT NEWID(), WorkspaceId, N'System', N'workspace.role.permissions.extended', N'ApplicationRole', CONVERT(nvarchar(120),Id),
                  SYSUTCDATETIME(), N'{"AddedPermissions":["crm.pipelines.manage","crm.custom-fields.manage"]}' FROM AspNetRoles
                WHERE IsSystemRole = 1 AND DisplayName IN (N'Owner',N'Admin');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE PermissionKey IN ('crm.pipelines.manage','crm.custom-fields.manage');");
            migrationBuilder.DropTable(
                name: "CrmActivities");

            migrationBuilder.DropTable(
                name: "CrmOpportunities");

            migrationBuilder.DropTable(
                name: "CrmStageRequiredFields");

            migrationBuilder.DropTable(
                name: "CrmContacts");

            migrationBuilder.DropTable(
                name: "CrmCustomFields");

            migrationBuilder.DropTable(
                name: "CrmPipelineStages");

            migrationBuilder.DropTable(
                name: "CrmCompanies");

            migrationBuilder.DropTable(
                name: "CrmPipelines");
        }
    }
}
