using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class LeadRoutingPhaseThree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicIntakeTokenHash",
                table: "Workspaces",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "CrmActivities",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "NotificationEmail",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeAvailabilities",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAvailabilities", x => new { x.WorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_EmployeeAvailabilities_AspNetUsers_UserId_WorkspaceId",
                        columns: x => new { x.UserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAvailabilities_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeadAssignmentConfigurations",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxOpenLeadsPerEmployee = table.Column<int>(type: "int", nullable: false),
                    AcknowledgementWindowSeconds = table.Column<int>(type: "int", nullable: false),
                    IdleWindowSeconds = table.Column<int>(type: "int", nullable: false),
                    RetryIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    EscalationUserIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EscalationIntervalsSecondsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoundRobinLastUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadAssignmentConfigurations", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentConfigurations_AspNetUsers_RoundRobinLastUserId_WorkspaceId",
                        columns: x => new { x.RoundRobinLastUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentConfigurations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeadIntakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceDetail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    MatchedContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadIntakes", x => x.Id);
                    table.UniqueConstraint("AK_LeadIntakes_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_LeadIntakes_CrmContacts_MatchedContactId_WorkspaceId",
                        columns: x => new { x.MatchedContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadIntakes_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceDetail = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgementDeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextEscalationAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EscalationCount = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttentionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.UniqueConstraint("AK_Leads_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_Leads_AspNetUsers_OwnerUserId_WorkspaceId",
                        columns: x => new { x.OwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_CrmContacts_ContactId_WorkspaceId",
                        columns: x => new { x.ContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_LeadIntakes_IntakeId_WorkspaceId",
                        columns: x => new { x.IntakeId, x.WorkspaceId },
                        principalTable: "LeadIntakes",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leads_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeadAssignmentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorWorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgementDeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadAssignmentHistory", x => x.Id);
                    table.CheckConstraint("CK_LeadAssignmentHistory_ActorWorkspace", "([ActorUserId] IS NULL AND [ActorWorkspaceId] IS NULL) OR ([ActorUserId] IS NOT NULL AND [ActorWorkspaceId] = [WorkspaceId])");
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistory_AspNetUsers_ActorUserId_ActorWorkspaceId",
                        columns: x => new { x.ActorUserId, x.ActorWorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistory_AspNetUsers_NewOwnerUserId_WorkspaceId",
                        columns: x => new { x.NewOwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistory_AspNetUsers_PreviousOwnerUserId_WorkspaceId",
                        columns: x => new { x.PreviousOwnerUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistory_Leads_LeadId_WorkspaceId",
                        columns: x => new { x.LeadId, x.WorkspaceId },
                        principalTable: "Leads",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadAssignmentHistory_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeadNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BodyText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_AspNetUsers_RecipientUserId_WorkspaceId",
                        columns: x => new { x.RecipientUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Leads_LeadId_WorkspaceId",
                        columns: x => new { x.LeadId, x.WorkspaceId },
                        principalTable: "Leads",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeadUserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadUserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadUserNotifications_AspNetUsers_RecipientUserId_WorkspaceId",
                        columns: x => new { x.RecipientUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadUserNotifications_Leads_LeadId_WorkspaceId",
                        columns: x => new { x.LeadId, x.WorkspaceId },
                        principalTable: "Leads",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeadUserNotifications_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_PublicIntakeTokenHash",
                table: "Workspaces",
                column: "PublicIntakeTokenHash",
                unique: true,
                filter: "[PublicIntakeTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAvailabilities_UserId_WorkspaceId",
                table: "EmployeeAvailabilities",
                columns: new[] { "UserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAvailabilities_WorkspaceId_Status_UserId",
                table: "EmployeeAvailabilities",
                columns: new[] { "WorkspaceId", "Status", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentConfigurations_RoundRobinLastUserId_WorkspaceId",
                table: "LeadAssignmentConfigurations",
                columns: new[] { "RoundRobinLastUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistory_ActorUserId_ActorWorkspaceId",
                table: "LeadAssignmentHistory",
                columns: new[] { "ActorUserId", "ActorWorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistory_LeadId_WorkspaceId",
                table: "LeadAssignmentHistory",
                columns: new[] { "LeadId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistory_NewOwnerUserId_WorkspaceId",
                table: "LeadAssignmentHistory",
                columns: new[] { "NewOwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistory_PreviousOwnerUserId_WorkspaceId",
                table: "LeadAssignmentHistory",
                columns: new[] { "PreviousOwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadAssignmentHistory_WorkspaceId_LeadId_AssignedAtUtc",
                table: "LeadAssignmentHistory",
                columns: new[] { "WorkspaceId", "LeadId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakes_MatchedContactId_WorkspaceId",
                table: "LeadIntakes",
                columns: new[] { "MatchedContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakes_WorkspaceId_IdempotencyKeyHash",
                table: "LeadIntakes",
                columns: new[] { "WorkspaceId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadIntakes_WorkspaceId_Status_ReceivedAtUtc",
                table: "LeadIntakes",
                columns: new[] { "WorkspaceId", "Status", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_LeadId_WorkspaceId",
                table: "LeadNotifications",
                columns: new[] { "LeadId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_RecipientUserId_WorkspaceId",
                table: "LeadNotifications",
                columns: new[] { "RecipientUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_Status_NextAttemptAtUtc_CreatedAtUtc",
                table: "LeadNotifications",
                columns: new[] { "Status", "NextAttemptAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_WorkspaceId_CreatedAtUtc",
                table: "LeadNotifications",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ContactId_WorkspaceId",
                table: "Leads",
                columns: new[] { "ContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_IntakeId_WorkspaceId",
                table: "Leads",
                columns: new[] { "IntakeId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OwnerUserId_WorkspaceId",
                table: "Leads",
                columns: new[] { "OwnerUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_IsClosed_NextEscalationAtUtc",
                table: "Leads",
                columns: new[] { "WorkspaceId", "IsClosed", "NextEscalationAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_LastActionAtUtc_IsClosed",
                table: "Leads",
                columns: new[] { "WorkspaceId", "LastActionAtUtc", "IsClosed" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_OwnerUserId_IsClosed_AcknowledgedAtUtc",
                table: "Leads",
                columns: new[] { "WorkspaceId", "OwnerUserId", "IsClosed", "AcknowledgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadUserNotifications_LeadId_WorkspaceId",
                table: "LeadUserNotifications",
                columns: new[] { "LeadId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadUserNotifications_RecipientUserId_WorkspaceId",
                table: "LeadUserNotifications",
                columns: new[] { "RecipientUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadUserNotifications_WorkspaceId_RecipientUserId_ReadAtUtc_CreatedAtUtc",
                table: "LeadUserNotifications",
                columns: new[] { "WorkspaceId", "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.Sql(@"
INSERT INTO [LeadAssignmentConfigurations]
    ([WorkspaceId], [MaxOpenLeadsPerEmployee], [AcknowledgementWindowSeconds], [IdleWindowSeconds], [RetryIntervalSeconds], [EscalationUserIdsJson], [EscalationIntervalsSecondsJson])
SELECT [Id], 10, 300, 86400, 30, N'[]', N'[300]'
FROM [Workspaces] w WHERE NOT EXISTS (SELECT 1 FROM [LeadAssignmentConfigurations] c WHERE c.[WorkspaceId] = w.[Id]);

INSERT INTO [EmployeeAvailabilities] ([WorkspaceId], [UserId], [Status], [ChangedAtUtc])
SELECT u.[WorkspaceId], u.[Id], N'Available', SYSUTCDATETIME()
FROM [AspNetUsers] u WHERE u.[IsActive] = 1
  AND NOT EXISTS (SELECT 1 FROM [EmployeeAvailabilities] a WHERE a.[WorkspaceId] = u.[WorkspaceId] AND a.[UserId] = u.[Id]);

DECLARE @NewPermissions TABLE ([PermissionKey] nvarchar(120), [DefaultUser] bit);
INSERT INTO @NewPermissions VALUES
    (N'leads.view', 1), (N'leads.view-all', 0), (N'leads.create', 1), (N'leads.edit-own', 1),
    (N'leads.acknowledge', 1), (N'leads.reassign', 0), (N'leads.availability.edit-own', 1),
    (N'leads.availability.manage', 0), (N'leads.notifications.view', 1);
INSERT INTO [RolePermissions] ([WorkspaceId], [RoleId], [PermissionKey], [GrantedAtUtc])
SELECT r.[WorkspaceId], r.[Id], p.[PermissionKey], SYSUTCDATETIME()
FROM [AspNetRoles] r CROSS JOIN @NewPermissions p
WHERE r.[IsSystemRole] = 1 AND (r.[DisplayName] IN (N'Owner', N'Admin') OR (r.[DisplayName] = N'User' AND p.[DefaultUser] = 1))
  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] rp WHERE rp.[WorkspaceId] = r.[WorkspaceId] AND rp.[RoleId] = r.[Id] AND rp.[PermissionKey] = p.[PermissionKey]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeAvailabilities");

            migrationBuilder.DropTable(
                name: "LeadAssignmentConfigurations");

            migrationBuilder.DropTable(
                name: "LeadAssignmentHistory");

            migrationBuilder.DropTable(
                name: "LeadNotifications");

            migrationBuilder.DropTable(
                name: "LeadUserNotifications");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "LeadIntakes");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_PublicIntakeTokenHash",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "PublicIntakeTokenHash",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "NotificationEmail",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "CrmActivities",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
