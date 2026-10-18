import { Component, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LeadDeskComponent } from './lead-desk.component';
import { WorkspaceAdminComponent } from './workspace-admin.component';
import { MessagingComponent } from './messaging.component';
import { InvitationJoinComponent } from './invitation-join.component';
import { RecordFieldsComponent } from './record-fields.component';
import { ApiActivity, ApiAssignedLead, ApiAuditEntry, ApiCalendarAppointment, ApiCalendarConnection, ApiCompany, ApiContact, ApiCustomField, ApiDashboard, ApiEmailConversation, ApiEmailSettings, ApiIntegrationStatus, ApiMember, ApiOpportunity, ApiPayment, ApiPipeline, ApiRecoveryLead, ApiSavedList, ApiSavedListResult, ApiStage, CurrentUser, CrmApiService } from './crm-api.service';

type Lead = {
  id: string; opportunityId: string; contactId: string; pipelineId: string; stageId: string; rowVersion: number[];
  ownerUserId: string; createdAtUtc: string; stageKind: 'Open' | 'Won' | 'Lost';
  name: string; company: string; email: string; phone: string; source: string; status: string;
  owner: string; initials: string; color: string; value: number; valueHidden: boolean;
  age: string; due: string; overdue: boolean; score: number; note: string;
};
type TeamPerson = { userId: string; name: string; role: string; initials: string; color: string; status: string; leads: string };

@Component({ selector: 'app-root', imports: [FormsModule, DatePipe, LeadDeskComponent, WorkspaceAdminComponent, MessagingComponent, InvitationJoinComponent, RecordFieldsComponent], templateUrl: './app.html', styleUrl: './app.css' })
export class App {
  protected isAuthenticated = signal(false);
  protected authChecking = signal(true);
  protected authBusy = signal(false);
  protected authMode = signal<'login' | 'register'>('login');
  protected authError = signal('');
  protected authSuccess = signal('');
  protected currentUser = signal<CurrentUser | null>(null);
  protected loginNotice = signal('');
  protected page = signal('Overview');
  protected search = signal('');
  protected selectedLead = signal<Lead | null>(null);
  protected showLeadModal = signal(false);
  protected showNotifications = signal(false);
  protected toast = signal('');
  protected mobileNav = signal(false);
  protected crmLoading = signal(false);
  protected crmError = signal('');
  protected authEmail = '';
  protected authPassword = '';
  protected rememberMe = true;
  protected registerWorkspace = '';
  protected registerName = '';
  protected newLeadName = '';
  protected newLeadEmail = '';
  protected newLeadCompany = '';
  protected newLeadValue = '';
  protected newLeadDescription = '';
  protected activities = signal<ApiActivity[]>([]);
  protected phase4Busy = signal(false);
  protected phase4Error = signal('');
  protected phase5Busy = signal(false);
  protected phase5Error = signal('');
  protected dashboard = signal<ApiDashboard | null>(null);
  protected recoveryItems = signal<ApiRecoveryLead[]>([]);
  protected savedLists = signal<ApiSavedList[]>([]);
  protected activeSavedList = signal<ApiSavedList | null>(null);
  protected savedListResults = signal<ApiSavedListResult[]>([]);
  protected savedListTotal = signal(0);
  protected savedListPage = signal(1);
  protected customFieldOptions = signal<ApiCustomField[]>([]);
  protected auditEntries = signal<ApiAuditEntry[]>([]);
  protected auditTotal = signal(0);
  protected auditPage = signal(1);
  protected auditEventFilter = '';
  protected auditTargetFilter = '';
  protected auditActorFilter = '';
  protected auditSearch = '';
  protected auditFromDate = '';
  protected auditToDate = '';
  protected selectedContact = signal<ApiContact | null>(null);
  protected contactOpportunities = signal<ApiOpportunity[]>([]);
  protected contactActivities = signal<ApiActivity[]>([]);
  protected contactConversations = signal<ApiEmailConversation[]>([]);
  protected contactPayments = signal<ApiPayment[]>([]);
  protected contactAppointments = signal<ApiCalendarAppointment[]>([]);
  protected dashboardStageFilter = signal('');
  protected dashboardCardVisibility = signal<Record<string, boolean>>({});
  protected savedListName = '';
  protected savedListType: 'Contact' | 'Opportunity' | 'Lead' = 'Opportunity';
  protected savedListSearch = '';
  protected savedListSource = '';
  protected savedListOwner = '';
  protected savedListPipeline = '';
  protected savedListStage = '';
  protected savedListFromDate = '';
  protected savedListToDate = '';
  protected savedListCustomField = '';
  protected savedListCustomValue = '';
  protected savedListShared = false;
  protected integrationStatuses = signal<ApiIntegrationStatus[]>([]);
  protected emailSettings = signal<ApiEmailSettings | null>(null);
  protected integrationHistory = signal<Array<{ id: string; provider: string; eventType: string; status: string; summary: string; occurredAtUtc: string }>>([]);
  protected emailConversations = signal<ApiEmailConversation[]>([]);
  protected activeConversation = signal<ApiEmailConversation | null>(null);
  protected emailReplyText = '';
  protected payments = signal<ApiPayment[]>([]);
  protected returnedPayment = signal<ApiPayment | null>(null);
  protected paymentLead = signal<Lead | null>(null);
  protected calendarConnection = signal<ApiCalendarConnection | null>(null);
  protected appointments = signal<ApiCalendarAppointment[]>([]);
  protected linkedLead = signal<ApiAssignedLead | null>(null);
  protected emailHost = '';
  protected emailPort = 587;
  protected emailFromAddress = '';
  protected emailEnableSsl = true;
  protected emailUsername = '';
  protected emailPassword = '';
  protected emailWebhookSecret = '';
  protected appointmentTitle = '';
  protected appointmentDescription = '';
  protected appointmentContactId = '';
  protected appointmentOpportunityId = '';
  protected appointmentAttendeeEmail = '';
  protected appointmentStartsAt = '';
  protected appointmentEndsAt = '';
  protected editingAppointmentId = '';
  protected calendarCanUse = computed(() => this.can('calendar.use'));
  protected canManageIntegrations = computed(() => this.can('integrations.manage'));
  protected filteredConversations = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.emailConversations().filter(item => !term || `${item.subject} ${item.participantEmail}`.toLowerCase().includes(term));
  });
  protected leads = signal<Lead[]>([]);
  protected pipelines = signal<ApiPipeline[]>([]);
  protected contacts = signal<ApiContact[]>([]);
  protected companies = signal<ApiCompany[]>([]);
  protected team = signal<TeamPerson[]>([]);
  protected workspaceMembers = signal<ApiMember[]>([]);
  protected activePipeline = computed(() => this.pipelines().find(pipeline => pipeline.isDefault) ?? this.pipelines()[0] ?? null);
  protected columns = computed(() => this.activePipeline()?.stages.slice().sort((a, b) => a.position - b.position) ?? []);
  protected savedListStages(): ApiStage[] {
    const pipeline = this.pipelines().find(item => item.id === this.savedListPipeline) ?? this.activePipeline();
    return pipeline?.stages.slice().sort((a, b) => a.position - b.position) ?? [];
  }
  protected readonly auditTargetTypes = ['Contact', 'Opportunity', 'Lead', 'SavedList', 'PaymentRecord', 'CalendarAppointment', 'CalendarConnection', 'EmailMessage', 'WorkspaceIntegration'];
  protected get pipelineName(): string { return this.activePipeline()?.name ?? 'Pipeline'; }
  protected filteredLeads = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.leads().filter(lead => {
      const matchesQuery = !query || `${lead.name} ${lead.company} ${lead.email} ${lead.source} ${lead.status}`.toLowerCase().includes(query);
      return matchesQuery && (!this.dashboardStageFilter() || lead.stageId === this.dashboardStageFilter());
    });
  });
  protected filteredContacts = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.contacts().filter(contact => !query || `${contact.displayName} ${contact.email ?? ''} ${contact.phone ?? ''}`.toLowerCase().includes(query));
  });
  protected readonly navMain = [
    { name: 'Overview', icon: '⌂' }, { name: 'Leads', icon: '◷' }, { name: 'WhatsApp', icon: '☏' }, { name: 'Opportunities', icon: '▤' }, { name: 'Pipeline', icon: '▥' }, { name: 'Payments', icon: '＄' },
    { name: 'Recovery queue', icon: '↗' }, { name: 'Contacts', icon: '♧' }, { name: 'Inbox', icon: '✉' }, { name: 'Calendar', icon: '▦' }
  ];
  protected readonly navManage = [
    { name: 'Team', icon: '♙' }, { name: 'Reports', icon: '▧' }, { name: 'Saved lists', icon: '☷' },
    { name: 'Audit history', icon: '◷' }, { name: 'Integrations', icon: '⌘' }, { name: 'Settings', icon: '⚙' }
  ];

  private readonly refreshTimer: number;
  constructor(private readonly api: CrmApiService) {
    void this.checkAuthentication();
    this.refreshTimer = window.setInterval(() => {
      if (!this.isAuthenticated() || document.visibilityState !== 'visible') return;
      if (this.page() === 'Overview' || this.page() === 'Reports') void this.loadDashboard();
      else if (this.page() === 'Inbox') void this.loadInboxData();
      else if (this.page() === 'Recovery queue') void this.loadRecoveryData();
    }, 30000);
  }

  ngOnDestroy(): void { window.clearInterval(this.refreshTimer); }

  private async checkAuthentication(): Promise<void> {
    try {
      const user = await this.api.me();
      this.currentUser.set(user);
      this.isAuthenticated.set(true);
      await this.loadCrmData();
      await this.handlePageReturn();
      if (this.page() === 'Overview') void this.loadDashboard();
    } catch (error) {
      if (!(error instanceof Error && 'status' in error && (error as { status: number }).status === 401)) {
        this.authError.set('The API could not be reached. Start the ASP.NET API, then try again.');
      }
    } finally { this.authChecking.set(false); }
  }

  protected async submitAuth(): Promise<void> {
    this.authError.set(''); this.authSuccess.set('');
    if (!this.authEmail.trim() || !this.authPassword) { this.authError.set('Enter your email and password to continue.'); return; }
    if (this.authMode() === 'register' && (!this.registerWorkspace.trim() || !this.registerName.trim())) {
      this.authError.set('Enter your workspace name and your name to create an account.'); return;
    }
    if (this.authMode() === 'register' && this.authPassword.length < 12) {
      this.authError.set('Choose a password with at least 12 characters.'); return;
    }
    this.authBusy.set(true);
    try {
      const user = this.authMode() === 'login'
        ? await this.api.login(this.authEmail.trim(), this.authPassword, this.rememberMe)
        : await this.api.register(this.registerWorkspace.trim(), this.registerName.trim(), this.authEmail.trim(), this.authPassword);
      this.currentUser.set(user); this.isAuthenticated.set(true); this.authPassword = '';
      this.authSuccess.set(''); this.page.set('Overview');
      await this.loadCrmData();
      await this.handlePageReturn();
      if (this.page() === 'Overview') void this.loadDashboard();
    } catch (error) {
      this.authError.set(this.errorMessage(error, 'We could not sign you in. Check your details and try again.'));
    } finally { this.authBusy.set(false); }
  }

  protected switchAuthMode(mode: 'login' | 'register'): void {
    this.authMode.set(mode); this.authError.set(''); this.authSuccess.set('');
  }

  protected async signOut(): Promise<void> {
    try { await this.api.logout(); }
    catch (error) { this.notify(this.errorMessage(error, 'Could not sign out. Please try again.')); return; }
    this.currentUser.set(null); this.isAuthenticated.set(false); this.leads.set([]); this.contacts.set([]); this.pipelines.set([]);
  }

  protected async loadCrmData(): Promise<void> {
    this.crmLoading.set(true); this.crmError.set('');
    try {
      const [contactsPage, opportunitiesPage, pipelinesPage] = await Promise.all([
        this.api.contacts(), this.api.opportunities(), this.api.pipelines()
      ]);
      this.contacts.set(contactsPage.items);
      this.pipelines.set(pipelinesPage.items);
      const [companiesResult, membersResult] = await Promise.allSettled([this.api.companies(), this.api.members()]);
      const companies = companiesResult.status === 'fulfilled' ? companiesResult.value.items : [];
      const members = membersResult.status === 'fulfilled' ? membersResult.value : [];
      this.workspaceMembers.set(members);
      this.companies.set(companies);
      const user = this.currentUser();
      const contactById = new Map(contactsPage.items.map(contact => [contact.id, contact]));
      const companyById = new Map(companies.map(company => [company.id, company.name]));
      const memberById = new Map(members.map(member => [member.userId, member]));
      const stages = new Map(pipelinesPage.items.flatMap(pipeline => pipeline.stages.map(stage => [stage.id, stage] as const)));
      this.leads.set(opportunitiesPage.items.map(opportunity => this.toLead(opportunity, contactById, companyById, memberById, stages, user)));
      this.team.set(members.filter(member => member.isActive).map(member => this.toTeamPerson(member, opportunitiesPage.items)));
      if (companiesResult.status === 'rejected' || membersResult.status === 'rejected') {
        this.crmError.set('Some workspace details are hidden by your permissions. You can still use the records you can access.');
      }
    } catch (error) {
      this.crmError.set(this.errorMessage(error, 'Could not load CRM records. Try refreshing the page.'));
    } finally { this.crmLoading.set(false); }
  }

  protected can(permission: string): boolean { return this.currentUser()?.permissions.includes(permission) ?? false; }

  protected navAllowed(name: string): boolean {
    if (name === 'Leads') return this.can('leads.view');
    if (name === 'WhatsApp') return this.can('whatsapp.inbox.view');
    if (name === 'Reports') return this.can('dashboard.view');
    if (name === 'Saved lists') return this.can('crm.contacts.view') || this.can('crm.opportunities.view') || this.can('leads.view');
    if (name === 'Audit history') return this.can('audit.view');
    if (name === 'Integrations') return this.can('integrations.manage');
    if (name === 'Recovery queue') return this.can('leads.reassign');
    if (name === 'Team') return this.can('workspace.members.manage') || this.can('leads.view-all');
    if (name === 'Settings') return this.can('crm.pipelines.manage') || this.can('crm.custom-fields.manage') || this.can('assignment.rules.manage');
    return true;
  }

  protected async loadDashboard(): Promise<void> {
    if (!this.can('dashboard.view')) return;
    this.phase5Busy.set(true); this.phase5Error.set('');
    try {
      this.dashboard.set(await this.api.dashboardOverview());
      const user = this.currentUser();
      if (user) {
        const stored = localStorage.getItem(`leadflow.dashboard.cards:${user.workspaceId}:${user.userId}`);
        if (stored) this.dashboardCardVisibility.set(JSON.parse(stored) as Record<string, boolean>);
      }
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not load dashboard metrics.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected dashboardCardVisible(key: string): boolean { return this.dashboardCardVisibility()[key] !== false; }

  protected toggleDashboardCard(key: string): void {
    const next = { ...this.dashboardCardVisibility(), [key]: !this.dashboardCardVisible(key) };
    this.dashboardCardVisibility.set(next);
    const user = this.currentUser();
    if (user) localStorage.setItem(`leadflow.dashboard.cards:${user.workspaceId}:${user.userId}`, JSON.stringify(next));
  }

  protected durationLabel(seconds?: number | null): string {
    if (seconds == null || !Number.isFinite(seconds)) return 'No acknowledged leads yet';
    if (seconds < 60) return `${Math.max(0, Math.round(seconds))} sec`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    return `${hours} hr ${minutes % 60} min`;
  }

  protected maxStageCount(stages: ApiDashboard['pipelineByStage']): number {
    return Math.max(1, ...stages.map(stage => stage.opportunityCount));
  }

  protected auditLastPage(): number { return Math.max(1, Math.ceil(this.auditTotal() / 50)); }

  protected async changeAuditPage(delta: number): Promise<void> {
    this.auditPage.update(page => Math.min(this.auditLastPage(), Math.max(1, page + delta)));
    await this.loadAudit();
  }

  protected async loadRecoveryData(): Promise<void> {
    if (!this.can('leads.reassign')) { this.phase5Error.set('Your role cannot view the workspace recovery queue.'); return; }
    this.phase5Busy.set(true); this.phase5Error.set('');
    try { this.recoveryItems.set((await this.api.recoveryLeads()).items); }
    catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not load the recovery queue.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected async loadSavedLists(): Promise<void> {
    this.phase5Busy.set(true); this.phase5Error.set('');
    try {
      this.savedLists.set(await this.api.savedLists());
      const selected = this.activeSavedList();
      const next = selected ? this.savedLists().find(x => x.id === selected.id) : this.savedLists()[0];
      if (next) await this.selectSavedList(next);
      else { this.activeSavedList.set(null); this.savedListResults.set([]); this.savedListTotal.set(0); }
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not load saved lists.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected async selectSavedList(list: ApiSavedList): Promise<void> {
    this.activeSavedList.set(list); this.phase5Error.set(''); this.savedListPage.set(1);
    this.savedListName = list.name; this.savedListType = list.entityType;
    this.savedListSearch = list.filters.search ?? ''; this.savedListSource = list.filters.source ?? '';
    this.savedListOwner = list.filters.ownerUserId ?? ''; this.savedListPipeline = list.filters.pipelineId ?? '';
    this.savedListStage = list.filters.stageId ?? '';
    this.savedListFromDate = this.utcToDateInput(list.filters.createdFromUtc);
    this.savedListToDate = this.utcToDateInput(list.filters.createdToUtc, true);
    this.savedListCustomField = list.filters.customFieldId ?? '';
    this.savedListCustomValue = list.filters.customFieldValue ?? ''; this.savedListShared = list.isShared;
    await this.loadSavedListCustomFields();
    await this.loadSavedListResults();
  }

  protected async loadSavedListResults(): Promise<void> {
    const list = this.activeSavedList(); if (!list) return;
    this.phase5Busy.set(true);
    try {
      const result = await this.api.savedListResults(list.id, this.savedListPage());
      this.savedListResults.set(result.items); this.savedListTotal.set(result.totalCount);
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not load saved-list results.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected async changeSavedListPage(delta: number): Promise<void> {
    this.savedListPage.update(page => Math.min(this.savedListLastPage(), Math.max(1, page + delta)));
    await this.loadSavedListResults();
  }

  protected savedListLastPage(): number { return Math.max(1, Math.ceil(this.savedListTotal() / 100)); }

  protected async loadSavedListCustomFields(): Promise<void> {
    if (this.savedListType === 'Lead') { this.customFieldOptions.set([]); return; }
    try {
      this.customFieldOptions.set(await this.api.customFields(this.savedListType, this.savedListType === 'Opportunity' ? this.savedListPipeline || undefined : undefined));
    } catch { this.customFieldOptions.set([]); }
  }

  protected async saveSavedList(): Promise<void> {
    if (!this.savedListName.trim()) { this.phase5Error.set('Enter a name for this saved list.'); return; }
    this.phase5Busy.set(true); this.phase5Error.set('');
    try {
      const filters = {
        search: this.savedListSearch.trim() || undefined,
        source: this.savedListType === 'Lead' ? this.savedListSource.trim() || undefined : undefined,
        ownerUserId: this.savedListOwner || undefined,
        pipelineId: this.savedListType === 'Opportunity' ? this.savedListPipeline || undefined : undefined,
        stageId: this.savedListType === 'Opportunity' ? this.savedListStage || undefined : undefined,
        createdFromUtc: this.dateInputToUtc(this.savedListFromDate) || undefined,
        createdToUtc: this.dateInputToUtc(this.savedListToDate, true) || undefined,
        customFieldId: this.savedListCustomField || undefined,
        customFieldValue: this.savedListCustomField ? this.savedListCustomValue : undefined
      };
      const payload = { name: this.savedListName.trim(), entityType: this.savedListType, filters, isShared: this.savedListShared };
      const selected = this.activeSavedList();
      const saved = selected && selected.ownerUserId === this.currentUser()?.userId
        ? await this.api.updateSavedList(selected.id, { ...payload, rowVersion: selected.rowVersion })
        : await this.api.createSavedList(payload);
      await this.loadSavedLists();
      const refreshed = this.savedLists().find(x => x.id === saved.id) ?? saved;
      await this.selectSavedList(refreshed);
      this.notify(selected?.id === saved.id ? 'Saved list updated.' : 'Saved list stored. Its results update from workspace records each time it opens.');
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not save this list.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected async deleteSavedList(): Promise<void> {
    const selected = this.activeSavedList(); if (!selected) return;
    this.phase5Busy.set(true); this.phase5Error.set('');
    try {
      await this.api.deleteSavedList(selected.id); this.activeSavedList.set(null); this.savedListResults.set([]);
      await this.loadSavedLists(); this.notify('Saved list deleted.');
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not delete this saved list.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected async loadAudit(): Promise<void> {
    if (!this.can('audit.view')) { this.phase5Error.set('Your role cannot view the audit history.'); return; }
    this.phase5Busy.set(true); this.phase5Error.set('');
    try {
      const result = await this.api.auditEntries({
        page: this.auditPage(), eventType: this.auditEventFilter.trim() || undefined,
        targetType: this.auditTargetFilter || undefined, actorUserId: this.auditActorFilter || undefined,
        search: this.auditSearch.trim() || undefined,
        fromUtc: this.dateInputToUtc(this.auditFromDate) || undefined,
        toUtc: this.dateInputToUtc(this.auditToDate, true) || undefined
      });
      this.auditEntries.set(result.items); this.auditTotal.set(result.totalCount);
    } catch (error) { this.phase5Error.set(this.errorMessage(error, 'Could not load audit history.')); }
    finally { this.phase5Busy.set(false); }
  }

  protected formatAuditData(value: unknown): string { return value == null ? '—' : JSON.stringify(value); }

  protected async openContact(contact: ApiContact): Promise<void> {
    this.selectedContact.set(contact); this.contactActivities.set([]); this.contactConversations.set([]);
    this.contactPayments.set([]); this.contactAppointments.set([]); this.contactOpportunities.set([]);
    await this.loadContact360();
  }

  protected async loadContact360(): Promise<void> {
    const contact = this.selectedContact(); if (!contact) return;
    this.phase5Busy.set(true); this.phase5Error.set('');
    const tasks: Promise<unknown>[] = [
      this.api.contactActivities(contact.id),
      this.api.opportunitiesForContact(contact.id)
    ];
    let emailIndex = -1, calendarIndex = -1;
    if (this.can('email.inbox.view') && contact.email) { tasks.push(this.api.emailConversations(contact.email)); emailIndex = tasks.length - 1; }
    if (this.can('calendar.use')) { tasks.push(this.api.calendarAppointments()); calendarIndex = tasks.length - 1; }
    const results = await Promise.allSettled(tasks);
    if (results[0].status === 'fulfilled') this.contactActivities.set((results[0].value as { items: ApiActivity[] }).items);
    if (results[1].status === 'fulfilled') this.contactOpportunities.set((results[1].value as { items: ApiOpportunity[] }).items);
    const emailResult = emailIndex >= 0 ? results[emailIndex] : undefined;
    if (emailResult?.status === 'fulfilled') {
      const items = (emailResult.value as { items: ApiEmailConversation[] }).items;
      this.contactConversations.set(items.filter(x => x.contactId === contact.id ||
        contact.email?.toLowerCase() === x.participantEmail.toLowerCase()));
    }
    const calendarResult = calendarIndex >= 0 ? results[calendarIndex] : undefined;
    if (calendarResult?.status === 'fulfilled')
      this.contactAppointments.set((calendarResult.value as ApiCalendarAppointment[]).filter(x => x.contactId === contact.id));
    const opportunityRows = this.contactOpportunities();
    if (this.can('crm.opportunities.value.view')) {
      const paymentResults = await Promise.allSettled(opportunityRows.map(x => this.api.payments(x.id)));
      this.contactPayments.set(paymentResults.flatMap(result => result.status === 'fulfilled' ? result.value : []));
    }
    const failed = results.some((result, index) => result.status === 'rejected' && (index < 2 || index === emailIndex || index === calendarIndex));
    if (failed) this.phase5Error.set('Some customer history is unavailable for your current permissions or provider connections.');
    this.phase5Busy.set(false);
  }

  protected closeContact(): void {
    this.selectedContact.set(null); this.contactActivities.set([]); this.contactConversations.set([]);
    this.contactPayments.set([]); this.contactAppointments.set([]); this.contactOpportunities.set([]);
  }

  protected dateInputToUtc(value: string, inclusiveEnd = false): string | null {
    if (!value) return null;
    const local = new Date(`${value}T00:00:00+05:00`);
    if (inclusiveEnd) local.setTime(local.getTime() + 24 * 60 * 60 * 1000);
    return local.toISOString();
  }

  private utcToDateInput(value?: string | null, exclusiveEnd = false): string {
    if (!value) return '';
    const date = new Date(value);
    if (exclusiveEnd) date.setTime(date.getTime() - 24 * 60 * 60 * 1000);
    return this.pakistanUtcToInput(date.toISOString()).slice(0, 10);
  }

  private readDashboardCardVisibility(): Record<string, boolean> {
    const user = this.currentUser();
    const stored = user ? localStorage.getItem(`leadflow.dashboard.cards:${user.workspaceId}:${user.userId}`) : null;
    try { return stored ? JSON.parse(stored) as Record<string, boolean> : {}; } catch { return {}; }
  }

  protected async loadIntegrationData(): Promise<void> {
    if (!this.canManageIntegrations()) { this.phase4Error.set('Ask a workspace owner or admin to manage connected services.'); return; }
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const [statuses, settings, history] = await Promise.all([
        this.api.integrationStatuses(), this.api.emailSettings(), this.api.integrationHistory()
      ]);
      this.integrationStatuses.set(statuses); this.emailSettings.set(settings); this.integrationHistory.set(history);
      this.emailHost = settings.host ?? ''; this.emailPort = settings.port ?? 587;
      this.emailFromAddress = settings.fromAddress ?? ''; this.emailEnableSsl = settings.enableSsl;
      this.emailUsername = ''; this.emailPassword = ''; this.emailWebhookSecret = '';
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not load integration settings.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async saveEmailSettings(): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const input: { host: string; port: number; fromAddress: string; enableSsl: boolean; username?: string; password?: string; inboundWebhookSecret?: string } = {
        host: this.emailHost.trim(), port: Number(this.emailPort), fromAddress: this.emailFromAddress.trim(), enableSsl: this.emailEnableSsl
      };
      if (this.emailUsername.trim()) input.username = this.emailUsername.trim();
      if (this.emailPassword) input.password = this.emailPassword;
      if (this.emailWebhookSecret) input.inboundWebhookSecret = this.emailWebhookSecret;
      this.emailSettings.set(await this.api.saveEmailSettings(input));
      this.emailPassword = ''; this.emailWebhookSecret = '';
      await this.loadIntegrationData();
      this.notify('Workspace email settings saved. Send a test email to confirm delivery.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not save email settings.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async testEmailConnection(): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const result = await this.api.testEmail(); this.emailSettings.set(result);
      await this.loadIntegrationData();
      if (result.status === 'Connected') this.notify('Test email delivered to your account.');
      else this.phase4Error.set(result.lastError || 'Email delivery failed. Check the SMTP host, port, TLS, and credentials.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Email delivery test failed.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async loadInboxData(): Promise<void> {
    if (!this.can('email.inbox.view')) { this.phase4Error.set('Your account does not have access to the email inbox.'); return; }
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const page = await this.api.emailConversations(); this.emailConversations.set(page.items);
      if (this.activeConversation()) {
        const updated = await this.api.emailConversation(this.activeConversation()!.id);
        this.activeConversation.set(updated);
      }
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not load email conversations.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async selectConversation(conversation: ApiEmailConversation): Promise<void> {
    this.activeConversation.set(conversation); this.emailReplyText = ''; this.phase4Error.set('');
    try { this.activeConversation.set(await this.api.emailConversation(conversation.id)); }
    catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not load this email thread.')); }
  }

  protected async sendEmailReply(): Promise<void> {
    const thread = this.activeConversation();
    if (!thread || !this.emailReplyText.trim()) return;
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      await this.api.replyToEmail(thread.id, this.emailReplyText.trim()); this.emailReplyText = '';
      await this.loadInboxData();
      this.notify('Email reply saved. Check its delivery status in the thread.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not send this email reply.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async retryEmailReply(messageId: string): Promise<void> {
    const thread = this.activeConversation();
    if (!thread) return;
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      await this.api.retryEmailReply(thread.id, messageId);
      this.activeConversation.set(await this.api.emailConversation(thread.id));
      this.notify('Email delivery retry completed. Check its status in the thread.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not retry this email.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async loadCalendarData(): Promise<void> {
    if (!this.calendarCanUse()) { this.phase4Error.set('Your account does not have calendar access. Ask a workspace owner to update your role.'); return; }
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const [connection, appointments] = await Promise.all([this.api.calendarStatus(), this.api.calendarAppointments()]);
      this.calendarConnection.set(connection); this.appointments.set(appointments);
      const query = new URLSearchParams(window.location.search).get('calendar');
      if (query === 'connected') this.notify('Google Calendar is connected.');
      else if (query && query !== 'connected') this.phase4Error.set(`Google Calendar connection returned: ${query.replaceAll('-', ' ')}.`);
      if (query) window.history.replaceState({}, document.title, window.location.pathname);
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not load calendar appointments.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async connectCalendar(): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try { const result = await this.api.calendarConnect(); window.location.assign(result.authorizationUrl); }
    catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not start Google Calendar connection.')); this.phase4Busy.set(false); }
  }

  protected async disconnectCalendar(): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try { await this.api.disconnectCalendar(); this.calendarConnection.set(await this.api.calendarStatus()); this.notify('Google Calendar disconnected.'); }
    catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not disconnect Google Calendar.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async scheduleAppointment(): Promise<void> {
    if (!this.appointmentTitle.trim() || !this.appointmentStartsAt || !this.appointmentEndsAt) {
      this.phase4Error.set('Enter a title, start time, and end time.'); return;
    }
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const input = {
        title: this.appointmentTitle.trim(), description: this.appointmentDescription.trim() || undefined,
        contactId: this.appointmentContactId || undefined, opportunityId: this.appointmentOpportunityId || undefined,
        attendeeEmail: this.appointmentAttendeeEmail.trim() || undefined,
        startsAt: this.pakistanInputToUtc(this.appointmentStartsAt), endsAt: this.pakistanInputToUtc(this.appointmentEndsAt)
      };
      const wasEditing = Boolean(this.editingAppointmentId);
      const result = wasEditing
        ? await this.api.rescheduleAppointment(this.editingAppointmentId, input)
        : await this.api.createAppointment(input);
      this.appointments.update(items => (items.some(item => item.id === result.id)
        ? items.map(item => item.id === result.id ? result : item)
        : [...items, result]).sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc)));
      this.appointmentTitle = ''; this.appointmentDescription = ''; this.appointmentContactId = '';
      this.appointmentOpportunityId = ''; this.appointmentAttendeeEmail = ''; this.editingAppointmentId = '';
      if (result.status === 'Failed') this.phase4Error.set(result.lastError || 'Google Calendar could not create this appointment.');
      else this.notify(wasEditing ? 'Appointment updated in Google Calendar.' : 'Appointment added to Google Calendar.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not create this appointment.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async cancelAppointment(appointment: ApiCalendarAppointment): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const updated = await this.api.cancelAppointment(appointment.id);
      this.appointments.update(items => items.map(item => item.id === updated.id ? updated : item));
      if (updated.status === 'Cancelled') this.notify('Appointment cancelled.');
      else this.phase4Error.set(updated.lastError || 'Could not cancel this appointment in Google Calendar.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not cancel this appointment.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected async retryAppointment(appointment: ApiCalendarAppointment): Promise<void> {
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      const updated = await this.api.retryAppointment(appointment.id);
      this.appointments.update(items => items.map(item => item.id === updated.id ? updated : item));
      if (updated.status === 'Failed') this.phase4Error.set(updated.lastError || 'Calendar sync failed again.');
      else this.notify('Appointment synced with Google Calendar.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not retry calendar sync.')); }
    finally { this.phase4Busy.set(false); }
  }

  protected editAppointment(appointment: ApiCalendarAppointment): void {
    this.editingAppointmentId = appointment.id; this.appointmentTitle = appointment.title;
    this.appointmentDescription = appointment.description ?? ''; this.appointmentContactId = appointment.contactId ?? '';
    this.appointmentOpportunityId = appointment.opportunityId ?? ''; this.appointmentAttendeeEmail = appointment.attendeeEmail ?? '';
    this.appointmentStartsAt = this.pakistanUtcToInput(appointment.startsAtUtc);
    this.appointmentEndsAt = this.pakistanUtcToInput(appointment.endsAtUtc);
    document.querySelector('.appointment-form')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected cancelAppointmentEdit(): void {
    this.editingAppointmentId = ''; this.appointmentTitle = ''; this.appointmentDescription = '';
    this.appointmentContactId = ''; this.appointmentOpportunityId = ''; this.appointmentAttendeeEmail = '';
    this.appointmentStartsAt = ''; this.appointmentEndsAt = '';
  }

  protected pakistanTime(value: string): string {
    return new Intl.DateTimeFormat('en-PK', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Karachi' }).format(new Date(value));
  }

  private pakistanInputToUtc(value: string): string {
    const [datePart, timePart] = value.split('T');
    const [year, month, day] = datePart.split('-').map(Number);
    const [hour, minute] = timePart.split(':').map(Number);
    const target = Date.UTC(year, month - 1, day, hour, minute);
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Karachi', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(target));
    const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
    const represented = Date.UTC(Number(values['year']), Number(values['month']) - 1, Number(values['day']), Number(values['hour']), Number(values['minute']));
    return new Date(target - (represented - target)).toISOString();
  }

  private pakistanUtcToInput(value: string): string {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Karachi', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(value));
    const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
    return `${values['year']}-${values['month']}-${values['day']}T${values['hour']}:${values['minute']}`;
  }

  private async handlePageReturn(): Promise<void> {
    const query = new URLSearchParams(window.location.search);
    if (query.has('calendar')) { this.page.set('Calendar'); await this.loadCalendarData(); }
    else if (query.has('lead')) {
      this.page.set('Opportunities');
      try { this.linkedLead.set(await this.api.assignedLead(query.get('lead')!)); }
      catch (error) { this.crmError.set(this.errorMessage(error, 'This lead link is unavailable in your workspace.')); }
      window.history.replaceState({}, document.title, window.location.pathname);
    }
    else if (query.has('payment')) {
      this.page.set('Payments');
      const sessionId = query.get('session_id');
      if (sessionId) {
        try {
          const payment = await this.api.paymentBySession(sessionId);
          this.returnedPayment.set(payment);
          const lead = this.leads().find(item => item.opportunityId === payment.opportunityId);
          if (lead) await this.showPayments(lead);
        } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Payment session details are not available in this workspace.')); }
      } else if (query.get('payment') === 'cancelled') this.phase4Error.set('Checkout was cancelled. The payment remains unpaid.');
      window.history.replaceState({}, document.title, window.location.pathname);
    }
  }

  private toLead(opportunity: ApiOpportunity, contacts: Map<string, ApiContact>, companies: Map<string, string>, members: Map<string, ApiMember>, stages: Map<string, ApiStage>, user: CurrentUser | null): Lead {
    const contact = contacts.get(opportunity.contactId);
    const stage = stages.get(opportunity.stageId);
    const owner = members.get(opportunity.ownerUserId);
    return {
      id: opportunity.id, opportunityId: opportunity.id, contactId: opportunity.contactId,
      pipelineId: opportunity.pipelineId, stageId: opportunity.stageId, rowVersion: opportunity.rowVersion,
      ownerUserId: opportunity.ownerUserId, createdAtUtc: opportunity.createdAtUtc, stageKind: stage?.kind ?? 'Open',
      name: contact?.displayName ?? opportunity.title, company: contact?.companyId ? companies.get(contact.companyId) ?? '' : '',
      email: contact?.email ?? '', phone: contact?.phone ?? '', source: 'CRM', status: stage?.name ?? 'Unstaged',
      owner: owner?.displayName ?? (user?.userId === opportunity.ownerUserId ? user.displayName : 'Workspace member'),
      initials: this.initials(owner?.displayName ?? user?.displayName ?? 'LF'), color: this.avatarColor(opportunity.ownerUserId),
      value: opportunity.value ?? 0, valueHidden: opportunity.valueHidden, age: this.relativeTime(opportunity.createdAtUtc),
      due: 'No response timer', overdue: false, score: 0, note: opportunity.description ?? opportunity.title
    };
  }

  private toTeamPerson(member: ApiMember, opportunities: ApiOpportunity[]): TeamPerson {
    const count = opportunities.filter(opportunity => opportunity.ownerUserId === member.userId).length;
    return { userId: member.userId, name: member.displayName, role: member.roles.join(', ') || 'Team member', initials: this.initials(member.displayName), color: this.avatarColor(member.userId), status: 'Active', leads: `${count} opportunities` };
  }

  protected initials(name: string): string { return name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || 'LF'; }
  protected companyName(id?: string | null): string { return this.companies().find(company => company.id === id)?.name ?? '—'; }
  protected memberName(id?: string | null): string { return this.team().find(person => person.userId === id)?.name ?? (id ? 'Workspace member' : '—'); }
  private avatarColor(seed: string): string { const colors = ['violet', 'blue', 'rose', 'amber']; let hash = 0; for (const char of seed) hash = (hash + char.charCodeAt(0)) % colors.length; return colors[hash]; }
  private relativeTime(value: string): string {
    const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000));
    if (minutes < 1) return 'Just now'; if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.floor(minutes / 60); if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;
    return `${Math.floor(hours / 24)} day${hours < 48 ? '' : 's'} ago`;
  }
  private errorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback;
  }

  protected money(value: number): string {
    const currency = this.currentUser()?.currencyCode ?? 'PKR';
    try { return new Intl.NumberFormat('en-PK', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value); }
    catch { return `${currency} ${value.toLocaleString()}`; }
  }
  protected countFor(stage: ApiStage): number { return this.leads().filter(lead => lead.stageId === stage.id).length; }
  protected totalFor(stage: ApiStage): number { return this.leads().filter(lead => lead.stageId === stage.id && !lead.valueHidden).reduce((sum, lead) => sum + lead.value, 0); }
  protected leadsFor(stage: ApiStage): Lead[] { return this.leads().filter(lead => lead.stageId === stage.id); }
  protected isOverdue(lead: Lead): boolean { return lead.overdue; }
  protected todayCount(): number { const today = new Date().toISOString().slice(0, 10); return this.leads().filter(lead => lead.createdAtUtc.slice(0, 10) === today).length; }
  protected openValue(): number { return this.leads().filter(lead => lead.stageKind === 'Open' && !lead.valueHidden).reduce((sum, lead) => sum + lead.value, 0); }
  protected setPage(name: string): void {
    this.page.set(name); this.mobileNav.set(false); this.selectedLead.set(null); this.phase4Error.set(''); this.phase5Error.set('');
    if (name === 'Inbox') void this.loadInboxData();
    else if (name === 'Calendar') void this.loadCalendarData();
    else if (name === 'Integrations') void this.loadIntegrationData();
    else if (name === 'Overview' || name === 'Reports') void this.loadDashboard();
    else if (name === 'Recovery queue') void this.loadRecoveryData();
    else if (name === 'Saved lists') void this.loadSavedLists();
    else if (name === 'Audit history') void this.loadAudit();
    else if (name === 'Contacts') this.closeContact();
  }
  protected openLead(lead: Lead): void { this.selectedLead.set(lead); void this.loadActivities(lead); void this.loadPayments(lead); }
  protected closeLead(): void { this.selectedLead.set(null); this.activities.set([]); }
  protected async loadPayments(lead: Lead): Promise<void> {
    if (!this.can('crm.opportunities.view')) { this.payments.set([]); return; }
    try { this.payments.set(await this.api.payments(lead.opportunityId)); }
    catch { this.payments.set([]); }
  }
  protected async showPayments(lead: Lead): Promise<void> {
    this.paymentLead.set(lead); this.payments.set([]); this.phase4Error.set('');
    await this.loadPayments(lead);
  }
  protected async createPaymentLink(lead: Lead): Promise<void> {
    if (!this.can('crm.opportunities.edit') || !this.can('crm.opportunities.value.view') || lead.value <= 0) {
      this.notify('A visible opportunity value and edit permission are required to create a payment link.'); return;
    }
    this.phase4Busy.set(true); this.phase4Error.set('');
    try {
      this.paymentLead.set(lead);
      const payment = await this.api.createPaymentCheckout(lead.opportunityId, crypto.randomUUID());
      this.payments.update(items => [payment, ...items.filter(item => item.id !== payment.id)]);
      if (payment.status === 'Failed') this.phase4Error.set(payment.lastError || 'Stripe could not create the checkout link.');
      else this.notify('Stripe checkout link created. Open it from the payment list below.');
    } catch (error) { this.phase4Error.set(this.errorMessage(error, 'Could not create a Stripe checkout link.')); }
    finally { this.phase4Busy.set(false); }
  }
  protected async loadActivities(lead: Lead): Promise<void> {
    try { this.activities.set((await this.api.activities(lead.opportunityId)).items); }
    catch { this.activities.set([]); }
  }
  protected acknowledge(lead: Lead): void { this.notify('Lead acknowledgement is not available in the current API yet.'); }
  protected async moveStage(lead: Lead, stage: ApiStage): Promise<void> {
    try {
      await this.api.moveOpportunity(lead.opportunityId, stage.id, lead.rowVersion);
      await this.loadCrmData();
      this.notify(`${lead.name} moved to ${stage.name}`);
    } catch (error) { this.notify(this.errorMessage(error, 'Could not move this opportunity.')); }
  }
  protected nextStage(stage: ApiStage): ApiStage | null {
    const stages = this.columns(); return stages[stages.findIndex(item => item.id === stage.id) + 1] ?? null;
  }
  protected async addLead(): Promise<void> {
    const user = this.currentUser(); const pipeline = this.activePipeline();
    const firstStage = pipeline?.stages.slice().sort((a, b) => a.position - b.position).find(stage => stage.kind === 'Open');
    if (!this.newLeadName.trim() || !this.newLeadEmail.trim()) { this.notify('Add a name and email to continue.'); return; }
    if (!user || !pipeline || !firstStage) { this.notify('Create a pipeline with an open stage before adding an opportunity.'); return; }
    try {
      let companyId: string | undefined;
      if (this.newLeadCompany.trim()) companyId = (await this.api.createCompany(this.newLeadCompany.trim())).id;
      const contact = await this.api.createContact({ displayName: this.newLeadName.trim(), email: this.newLeadEmail.trim(), companyId, ownerUserId: user.userId });
      await this.api.createOpportunity({
        title: this.newLeadCompany.trim() ? `${this.newLeadCompany.trim()} opportunity` : `${this.newLeadName.trim()} opportunity`,
        contactId: contact.id, pipelineId: pipeline.id, stageId: firstStage.id, ownerUserId: user.userId,
        value: this.newLeadValue.trim() ? Number(this.newLeadValue) : 0, description: this.newLeadDescription.trim()
      });
      this.newLeadName = ''; this.newLeadEmail = ''; this.newLeadCompany = ''; this.newLeadValue = ''; this.newLeadDescription = '';
      this.showLeadModal.set(false); this.setPage('Opportunities'); await this.loadCrmData(); this.notify('Opportunity created.');
    } catch (error) { this.notify(this.errorMessage(error, 'Could not create this opportunity.')); }
  }
  protected async addNote(lead: Lead, text: string): Promise<void> {
    if (!text.trim()) { this.notify('Write a note before saving.'); return; }
    try { await this.api.addOpportunityNote(lead.opportunityId, text.trim()); await this.loadActivities(lead); this.notify('Note added to the activity history.'); }
    catch (error) { this.notify(this.errorMessage(error, 'Could not add the note.')); }
  }
  protected notify(message: string): void { this.toast.set(message); setTimeout(() => this.toast.set(''), 2800); }
  protected sourceClass(source: string): string { return source.toLowerCase().replace(' ', '-'); }
}
