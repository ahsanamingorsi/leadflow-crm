namespace leadflow_crm.API.Authorization;

public static class PermissionCatalog
{
    public const string WorkspaceMembersManage = "workspace.members.manage";
    public const string WorkspaceRolesManage = "workspace.roles.manage";
    public const string ContactsView = "crm.contacts.view";
    public const string ContactsCreate = "crm.contacts.create";
    public const string ContactsEdit = "crm.contacts.edit";
    public const string ContactsDelete = "crm.contacts.delete";
    public const string ContactsExport = "crm.contacts.export";
    public const string CompaniesView = "crm.companies.view";
    public const string CompaniesCreate = "crm.companies.create";
    public const string CompaniesEdit = "crm.companies.edit";
    public const string CompaniesDelete = "crm.companies.delete";
    public const string CompaniesExport = "crm.companies.export";
    public const string OpportunitiesView = "crm.opportunities.view";
    public const string OpportunitiesCreate = "crm.opportunities.create";
    public const string OpportunitiesEdit = "crm.opportunities.edit";
    public const string OpportunitiesDelete = "crm.opportunities.delete";
    public const string OpportunitiesExport = "crm.opportunities.export";
    public const string OpportunityValueView = "crm.opportunities.value.view";
    public const string PipelinesManage = "crm.pipelines.manage";
    public const string CustomFieldsManage = "crm.custom-fields.manage";
    public const string LeadsView = "leads.view";
    public const string LeadsViewAll = "leads.view-all";
    public const string LeadsCreate = "leads.create";
    public const string LeadsEdit = "leads.edit-own";
    public const string LeadsAcknowledge = "leads.acknowledge";
    public const string LeadsReassign = "leads.reassign";
    public const string AvailabilityEditOwn = "leads.availability.edit-own";
    public const string AvailabilityManage = "leads.availability.manage";
    public const string LeadNotificationsView = "leads.notifications.view";
    public const string AssignmentRulesManage = "assignment.rules.manage";
    public const string IntegrationsManage = "integrations.manage";
    public const string EmailInboxView = "email.inbox.view";
    public const string EmailInboxReply = "email.inbox.reply";
    public const string WhatsAppView = "whatsapp.inbox.view";
    public const string WhatsAppReply = "whatsapp.inbox.reply";
    public const string CalendarUse = "calendar.use";
    public const string AuditView = "audit.view";
    public const string DashboardView = "dashboard.view";

    public static IReadOnlyList<string> All { get; } =
    [
        WorkspaceMembersManage,
        WorkspaceRolesManage,
        ContactsView,
        ContactsCreate,
        ContactsEdit,
        ContactsDelete,
        ContactsExport,
        CompaniesView,
        CompaniesCreate,
        CompaniesEdit,
        CompaniesDelete,
        CompaniesExport,
        OpportunitiesView,
        OpportunitiesCreate,
        OpportunitiesEdit,
        OpportunitiesDelete,
        OpportunitiesExport,
        OpportunityValueView,
        PipelinesManage,
        CustomFieldsManage,
        LeadsView,
        LeadsViewAll,
        LeadsCreate,
        LeadsEdit,
        LeadsAcknowledge,
        LeadsReassign,
        AvailabilityEditOwn,
        AvailabilityManage,
        LeadNotificationsView,
        AssignmentRulesManage,
        IntegrationsManage,
        EmailInboxView,
        EmailInboxReply,
        WhatsAppView,
        WhatsAppReply,
        CalendarUse,
        AuditView,
        DashboardView
    ];

    public static IReadOnlyList<string> DefaultUser { get; } =
    [
        ContactsView,
        ContactsCreate,
        ContactsEdit,
        CompaniesView,
        CompaniesCreate,
        CompaniesEdit,
        OpportunitiesView,
        OpportunitiesCreate,
        OpportunitiesEdit,
        LeadsView,
        LeadsCreate,
        LeadsEdit,
        LeadsAcknowledge,
        AvailabilityEditOwn,
        LeadNotificationsView,
        EmailInboxView,
        EmailInboxReply,
        WhatsAppView,
        WhatsAppReply,
        CalendarUse,
        DashboardView
    ];
}
