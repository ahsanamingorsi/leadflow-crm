import { Component, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiActivity, ApiCompany, ApiContact, ApiMember, ApiOpportunity, ApiPipeline, ApiStage, CurrentUser, CrmApiService } from './crm-api.service';

type Lead = {
  id: string; opportunityId: string; contactId: string; pipelineId: string; stageId: string; rowVersion: number[];
  ownerUserId: string; createdAtUtc: string; stageKind: 'Open' | 'Won' | 'Lost';
  name: string; company: string; email: string; phone: string; source: string; status: string;
  owner: string; initials: string; color: string; value: number; valueHidden: boolean;
  age: string; due: string; overdue: boolean; score: number; note: string;
};
type TeamPerson = { userId: string; name: string; role: string; initials: string; color: string; status: string; leads: string };

@Component({ selector: 'app-root', imports: [FormsModule, DatePipe], templateUrl: './app.html', styleUrl: './app.css' })
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
  protected leads = signal<Lead[]>([]);
  protected pipelines = signal<ApiPipeline[]>([]);
  protected contacts = signal<ApiContact[]>([]);
  protected companies = signal<ApiCompany[]>([]);
  protected team = signal<TeamPerson[]>([]);
  protected activePipeline = computed(() => this.pipelines().find(pipeline => pipeline.isDefault) ?? this.pipelines()[0] ?? null);
  protected columns = computed(() => this.activePipeline()?.stages.slice().sort((a, b) => a.position - b.position) ?? []);
  protected get pipelineName(): string { return this.activePipeline()?.name ?? 'Pipeline'; }
  protected filteredLeads = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.leads().filter(lead => {
      const matchesQuery = !query || `${lead.name} ${lead.company} ${lead.email} ${lead.source} ${lead.status}`.toLowerCase().includes(query);
      return matchesQuery;
    });
  });
  protected filteredContacts = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.contacts().filter(contact => !query || `${contact.displayName} ${contact.email ?? ''} ${contact.phone ?? ''}`.toLowerCase().includes(query));
  });
  protected readonly navMain = [
    { name: 'Overview', icon: '⌂' }, { name: 'Leads', icon: '▤' }, { name: 'Pipeline', icon: '▥' },
    { name: 'Recovery queue', icon: '↗' }, { name: 'Contacts', icon: '♧' }, { name: 'Calendar', icon: '▦' }
  ];
  protected readonly navManage = [
    { name: 'Team', icon: '♙' }, { name: 'Reports', icon: '▧' }, { name: 'Integrations', icon: '⌘' }, { name: 'Settings', icon: '⚙' }
  ];

  constructor(private readonly api: CrmApiService) { void this.checkAuthentication(); }

  private async checkAuthentication(): Promise<void> {
    try {
      const user = await this.api.me();
      this.currentUser.set(user);
      this.isAuthenticated.set(true);
      await this.loadCrmData();
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
  protected setPage(name: string): void { this.page.set(name); this.mobileNav.set(false); this.selectedLead.set(null); }
  protected openLead(lead: Lead): void { this.selectedLead.set(lead); void this.loadActivities(lead); }
  protected closeLead(): void { this.selectedLead.set(null); this.activities.set([]); }
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
      this.showLeadModal.set(false); this.setPage('Leads'); await this.loadCrmData(); this.notify('Opportunity created.');
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
