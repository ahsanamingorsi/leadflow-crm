using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace leadflow_crm.API.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFourIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarAppointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AttendeeEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GoogleEventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    HtmlLink = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarAppointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarAppointments_AspNetUsers_CreatedByUserId_WorkspaceId",
                        columns: x => new { x.CreatedByUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarAppointments_CrmContacts_ContactId_WorkspaceId",
                        columns: x => new { x.ContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarAppointments_CrmOpportunities_OpportunityId_WorkspaceId",
                        columns: x => new { x.OpportunityId, x.WorkspaceId },
                        principalTable: "CrmOpportunities",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarAppointments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CalendarId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarConnections_AspNetUsers_UserId_WorkspaceId",
                        columns: x => new { x.UserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarConnections_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarOAuthStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StateHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarOAuthStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarOAuthStates_AspNetUsers_UserId_WorkspaceId",
                        columns: x => new { x.UserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarOAuthStates_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalThreadId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ParticipantEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConversations", x => x.Id);
                    table.UniqueConstraint("AK_EmailConversations_Id_WorkspaceId", x => new { x.Id, x.WorkspaceId });
                    table.ForeignKey(
                        name: "FK_EmailConversations_CrmContacts_ContactId_WorkspaceId",
                        columns: x => new { x.ContactId, x.WorkspaceId },
                        principalTable: "CrmContacts",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailConversations_Leads_LeadId_WorkspaceId",
                        columns: x => new { x.LeadId, x.WorkspaceId },
                        principalTable: "Leads",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailConversations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSyncEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSyncEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationSyncEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StripeCheckoutSessionId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRecords_AspNetUsers_CreatedByUserId_WorkspaceId",
                        columns: x => new { x.CreatedByUserId, x.WorkspaceId },
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRecords_CrmOpportunities_OpportunityId_WorkspaceId",
                        columns: x => new { x.OpportunityId, x.WorkspaceId },
                        principalTable: "CrmOpportunities",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRecords_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    EncryptedSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceIntegrations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BodyText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(998)", maxLength: 998, nullable: true),
                    InReplyTo = table.Column<string>(type: "nvarchar(998)", maxLength: 998, nullable: true),
                    References = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailMessages_EmailConversations_ConversationId_WorkspaceId",
                        columns: x => new { x.ConversationId, x.WorkspaceId },
                        principalTable: "EmailConversations",
                        principalColumns: new[] { "Id", "WorkspaceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailMessages_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAppointments_ContactId_WorkspaceId",
                table: "CalendarAppointments",
                columns: new[] { "ContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAppointments_CreatedByUserId_WorkspaceId",
                table: "CalendarAppointments",
                columns: new[] { "CreatedByUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAppointments_OpportunityId_WorkspaceId",
                table: "CalendarAppointments",
                columns: new[] { "OpportunityId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAppointments_WorkspaceId_GoogleEventId",
                table: "CalendarAppointments",
                columns: new[] { "WorkspaceId", "GoogleEventId" },
                unique: true,
                filter: "[GoogleEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAppointments_WorkspaceId_StartsAtUtc_Status",
                table: "CalendarAppointments",
                columns: new[] { "WorkspaceId", "StartsAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarConnections_UserId_WorkspaceId",
                table: "CalendarConnections",
                columns: new[] { "UserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarConnections_WorkspaceId_UserId",
                table: "CalendarConnections",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOAuthStates_ExpiresAtUtc",
                table: "CalendarOAuthStates",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOAuthStates_StateHash",
                table: "CalendarOAuthStates",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOAuthStates_UserId_WorkspaceId",
                table: "CalendarOAuthStates",
                columns: new[] { "UserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOAuthStates_WorkspaceId",
                table: "CalendarOAuthStates",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailConversations_ContactId_WorkspaceId",
                table: "EmailConversations",
                columns: new[] { "ContactId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConversations_LeadId_WorkspaceId",
                table: "EmailConversations",
                columns: new[] { "LeadId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConversations_WorkspaceId_ExternalThreadId",
                table: "EmailConversations",
                columns: new[] { "WorkspaceId", "ExternalThreadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailConversations_WorkspaceId_LastMessageAtUtc",
                table: "EmailConversations",
                columns: new[] { "WorkspaceId", "LastMessageAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_ConversationId_WorkspaceId",
                table: "EmailMessages",
                columns: new[] { "ConversationId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_WorkspaceId_ConversationId_CreatedAtUtc",
                table: "EmailMessages",
                columns: new[] { "WorkspaceId", "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_WorkspaceId_ProviderMessageId",
                table: "EmailMessages",
                columns: new[] { "WorkspaceId", "ProviderMessageId" },
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncEvents_WorkspaceId_OccurredAtUtc",
                table: "IntegrationSyncEvents",
                columns: new[] { "WorkspaceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncEvents_WorkspaceId_Provider_ExternalEventId",
                table: "IntegrationSyncEvents",
                columns: new[] { "WorkspaceId", "Provider", "ExternalEventId" },
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_CreatedByUserId_WorkspaceId",
                table: "PaymentRecords",
                columns: new[] { "CreatedByUserId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_OpportunityId_WorkspaceId",
                table: "PaymentRecords",
                columns: new[] { "OpportunityId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_WorkspaceId_IdempotencyKey",
                table: "PaymentRecords",
                columns: new[] { "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_WorkspaceId_OpportunityId_CreatedAtUtc",
                table: "PaymentRecords",
                columns: new[] { "WorkspaceId", "OpportunityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_WorkspaceId_StripeCheckoutSessionId",
                table: "PaymentRecords",
                columns: new[] { "WorkspaceId", "StripeCheckoutSessionId" },
                unique: true,
                filter: "[StripeCheckoutSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_WorkspaceId_StripePaymentIntentId",
                table: "PaymentRecords",
                columns: new[] { "WorkspaceId", "StripePaymentIntentId" },
                filter: "[StripePaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceIntegrations_WorkspaceId_Provider",
                table: "WorkspaceIntegrations",
                columns: new[] { "WorkspaceId", "Provider" },
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO [RolePermissions] ([WorkspaceId], [RoleId], [PermissionKey], [GrantedAtUtc])
SELECT r.[WorkspaceId], r.[Id], p.[PermissionKey], SYSUTCDATETIME()
FROM [AspNetRoles] AS r
CROSS JOIN (VALUES
    (N'integrations.manage', N'Owner'),
    (N'integrations.manage', N'Admin'),
    (N'email.inbox.view', N'Owner'),
    (N'email.inbox.view', N'Admin'),
    (N'email.inbox.view', N'User'),
    (N'email.inbox.reply', N'Owner'),
    (N'email.inbox.reply', N'Admin'),
    (N'email.inbox.reply', N'User'),
    (N'calendar.use', N'Owner'),
    (N'calendar.use', N'Admin'),
    (N'calendar.use', N'User')
) AS p([PermissionKey], [RoleName])
WHERE r.[DisplayName] = p.[RoleName]
  AND NOT EXISTS (
      SELECT 1 FROM [RolePermissions] AS existing
      WHERE existing.[WorkspaceId] = r.[WorkspaceId]
        AND existing.[RoleId] = r.[Id]
        AND existing.[PermissionKey] = p.[PermissionKey]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM [RolePermissions]
WHERE [PermissionKey] IN (N'integrations.manage', N'email.inbox.view', N'email.inbox.reply', N'calendar.use');");

            migrationBuilder.DropTable(
                name: "CalendarAppointments");

            migrationBuilder.DropTable(
                name: "CalendarConnections");

            migrationBuilder.DropTable(
                name: "CalendarOAuthStates");

            migrationBuilder.DropTable(
                name: "EmailMessages");

            migrationBuilder.DropTable(
                name: "IntegrationSyncEvents");

            migrationBuilder.DropTable(
                name: "PaymentRecords");

            migrationBuilder.DropTable(
                name: "WorkspaceIntegrations");

            migrationBuilder.DropTable(
                name: "EmailConversations");
        }
    }
}
