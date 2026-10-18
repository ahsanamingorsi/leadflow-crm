import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

type Stage = 'New' | 'Contacted' | 'Meeting booked' | 'Proposal sent' | 'Won' | 'Lost';
type Lead = { id: number; name: string; company: string; email: string; phone: string; source: string; status: Stage; owner: string; initials: string; color: string; value: number; age: string; due: string; overdue: boolean; score: number; note: string };

@Component({ selector: 'app-root', imports: [FormsModule], templateUrl: './app.html', styleUrl: './app.css' })
export class App {
  protected isAuthenticated = signal(false);
  protected authChecking = signal(true);
  protected page = signal('Overview');
  protected search = signal('');
  protected filter = signal('All leads');
  protected selectedLead = signal<Lead | null>(null);
  protected showLeadModal = signal(false);
  protected showNotifications = signal(false);
  protected loginNotice = signal('');
  protected toast = signal('');
  protected mobileNav = signal(false);
  protected newLeadName = '';
  protected newLeadEmail = '';
  protected newLeadCompany = '';
  constructor() { void this.checkAuthentication(); }
  private async checkAuthentication(): Promise<void> {
    try {
      const response = await fetch('/api/auth/me', { credentials: 'include' });
      if (response.ok) this.isAuthenticated.set(true);
    } catch { /* The ASP.NET API may not be running while the UI is being previewed. */ }
    finally { this.authChecking.set(false); }
  }
  protected continueWithGoogle(): void { window.location.assign('/api/auth/google?returnUrl=%2F'); }
  protected previewDemo(): void { this.isAuthenticated.set(true); }
  protected readonly navMain = [
    { name: 'Overview', icon: '⌂' }, { name: 'Leads', icon: '▤', count: '12' },
    { name: 'Pipeline', icon: '▥' }, { name: 'Recovery queue', icon: '↗', count: '3', alert: true },
    { name: 'Contacts', icon: '♧' }, { name: 'Calendar', icon: '▦' }
  ];
  protected readonly navManage = [
    { name: 'Team', icon: '♙' }, { name: 'Reports', icon: '▧' }, { name: 'Integrations', icon: '⌘' }, { name: 'Settings', icon: '⚙' }
  ];
  protected leads = signal<Lead[]>([
    { id: 1, name: 'Olivia Rhye', company: 'Northstar Properties', email: 'olivia@northstar.co', phone: '+1 (415) 555-0132', source: 'Website', status: 'New', owner: 'Alex Morgan', initials: 'AM', color: 'violet', value: 24000, age: '4 min ago', due: 'in 6 min', overdue: false, score: 92, note: 'Interested in a 3-bedroom property in the Marina district.' },
    { id: 2, name: 'Phoenix Baker', company: 'Baker & Co.', email: 'phoenix@baker.co', phone: '+1 (415) 555-0188', source: 'WhatsApp', status: 'Contacted', owner: 'Drew Cano', initials: 'DC', color: 'blue', value: 12500, age: '18 min ago', due: 'Overdue 2m', overdue: true, score: 84, note: 'Asked about financing options and a site visit this week.' },
    { id: 3, name: 'Lana Steiner', company: 'Steiner Studio', email: 'lana@steiner.io', phone: '+1 (415) 555-0194', source: 'Referral', status: 'Meeting booked', owner: 'Kate Morrison', initials: 'KM', color: 'rose', value: 18500, age: '32 min ago', due: 'Acknowledged', overdue: false, score: 77, note: 'Referred by Jordan Lee. Looking to move before September.' },
    { id: 4, name: 'Demi Wilkinson', company: 'Demi Homes', email: 'demi@demihomes.com', phone: '+1 (415) 555-0111', source: 'Website', status: 'Proposal sent', owner: 'Alex Morgan', initials: 'AM', color: 'violet', value: 32000, age: '1 hour ago', due: 'Acknowledged', overdue: false, score: 96, note: 'Proposal shared. Follow up on Thursday afternoon.' },
    { id: 5, name: 'Candice Wu', company: 'Wu Ventures', email: 'candice@wuventures.com', phone: '+1 (415) 555-0108', source: 'WhatsApp', status: 'New', owner: 'Unassigned', initials: '—', color: 'gray', value: 8900, age: '1 hour ago', due: 'Overdue 12m', overdue: true, score: 68, note: 'Asked for availability near downtown.' },
    { id: 6, name: 'Natali Craig', company: 'Craig Family Trust', email: 'natali@craigft.com', phone: '+1 (415) 555-0164', source: 'Instagram', status: 'Contacted', owner: 'Drew Cano', initials: 'DC', color: 'blue', value: 14600, age: '2 hours ago', due: 'Acknowledged', overdue: false, score: 72, note: 'Prefers email communication. Budget up to $15k.' },
    { id: 7, name: 'Drew Cano', company: 'Cano Investments', email: 'drew@canoinv.com', phone: '+1 (415) 555-0160', source: 'Referral', status: 'Won', owner: 'Kate Morrison', initials: 'KM', color: 'rose', value: 27500, age: 'Yesterday', due: 'Acknowledged', overdue: false, score: 99, note: 'Deal closed. Welcome packet sent.' },
    { id: 8, name: 'Orlando Diggs', company: 'Diggs Group', email: 'orlando@diggs.co', phone: '+1 (415) 555-0140', source: 'Website', status: 'New', owner: 'Alex Morgan', initials: 'AM', color: 'violet', value: 11200, age: '3 hours ago', due: 'in 2 min', overdue: false, score: 63, note: 'Looking for a small commercial space in SoMa.' },
    { id: 9, name: 'Andi Lane', company: 'Lane & Partners', email: 'andi@lane.co', phone: '+1 (415) 555-0157', source: 'WhatsApp', status: 'Proposal sent', owner: 'Kate Morrison', initials: 'KM', color: 'rose', value: 22000, age: 'Yesterday', due: 'Acknowledged', overdue: false, score: 87, note: 'Comparing two options. Decision expected next week.' },
    { id: 10, name: 'Kate Morrison', company: 'Morrison Design', email: 'kate@morrison.design', phone: '+1 (415) 555-0129', source: 'Referral', status: 'Lost', owner: 'Drew Cano', initials: 'DC', color: 'blue', value: 7600, age: '2 days ago', due: 'Acknowledged', overdue: false, score: 42, note: 'Went with another provider. Revisit in Q4.' },
    { id: 11, name: 'Lindsay Walton', company: 'Walton Group', email: 'lindsay@walton.co', phone: '+1 (415) 555-0177', source: 'Website', status: 'Meeting booked', owner: 'Alex Morgan', initials: 'AM', color: 'violet', value: 15800, age: 'Yesterday', due: 'Acknowledged', overdue: false, score: 81, note: 'Site visit booked for Friday at 11:00 AM.' },
    { id: 12, name: 'Alec Whitten', company: 'Whitten Holdings', email: 'alec@whitten.com', phone: '+1 (415) 555-0120', source: 'Instagram', status: 'Contacted', owner: 'Kate Morrison', initials: 'KM', color: 'rose', value: 19800, age: '2 days ago', due: 'Acknowledged', overdue: false, score: 74, note: 'Sent brochure. Waiting on preferred locations.' }
  ]);
  protected filteredLeads = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.leads().filter(lead => {
      const matchesQuery = !query || `${lead.name} ${lead.company} ${lead.email} ${lead.source}`.toLowerCase().includes(query);
      const matchesFilter = this.filter() === 'All leads' || (this.filter() === 'Overdue' ? lead.overdue : lead.status === this.filter());
      return matchesQuery && matchesFilter;
    });
  });
  protected columns: Stage[] = ['New', 'Contacted', 'Meeting booked', 'Proposal sent'];
  protected readonly team = [
    { name: 'Alex Morgan', role: 'Sales lead', initials: 'AM', color: 'violet', status: 'Available', leads: '8 leads' },
    { name: 'Drew Cano', role: 'Sales associate', initials: 'DC', color: 'blue', status: 'On a call', leads: '6 leads' },
    { name: 'Kate Morrison', role: 'Sales associate', initials: 'KM', color: 'rose', status: 'Available', leads: '5 leads' },
    { name: 'Phoenix Baker', role: 'Sales associate', initials: 'PB', color: 'amber', status: 'On leave', leads: '3 leads' }
  ];
  protected money(value: number): string { return '$' + value.toLocaleString('en-US'); }
  protected countFor(status: Stage): number { return this.leads().filter(l => l.status === status).length; }
  protected totalFor(status: Stage): number { return this.leads().filter(l => l.status === status).reduce((sum, l) => sum + l.value, 0); }
  protected leadsFor(status: Stage): Lead[] { return this.leads().filter(lead => lead.status === status); }
  protected isOverdue(lead: Lead): boolean { return lead.overdue; }
  protected isInStage(lead: Lead, stage: Stage): boolean { return lead.status === stage; }
  protected nextStage(stage: Stage): Stage { return stage === 'New' ? 'Contacted' : 'Meeting booked'; }
  protected setPage(name: string): void { this.page.set(name); this.mobileNav.set(false); this.selectedLead.set(null); }
  protected openLead(lead: Lead): void { this.selectedLead.set(lead); }
  protected closeLead(): void { this.selectedLead.set(null); }
  protected acknowledge(lead: Lead): void {
    this.leads.update(items => items.map(item => item.id === lead.id ? { ...item, overdue: false, due: 'Acknowledged', status: item.status === 'New' ? 'Contacted' : item.status } : item));
    this.selectedLead.set(null); this.notify(`${lead.name} acknowledged`);
  }
  protected moveStage(lead: Lead, status: Stage): void {
    this.leads.update(items => items.map(item => item.id === lead.id ? { ...item, status, overdue: false, due: 'Acknowledged' } : item));
    this.notify(`${lead.name} moved to ${status}`);
  }
  protected addLead(): void {
    if (!this.newLeadName.trim() || !this.newLeadEmail.trim()) { this.notify('Add a name and email to continue'); return; }
    const name = this.newLeadName.trim();
    this.leads.update(items => [{ id: Date.now(), name, company: this.newLeadCompany.trim() || 'New company', email: this.newLeadEmail.trim(), phone: '—', source: 'Website', status: 'New', owner: 'Unassigned', initials: '—', color: 'gray', value: 0, age: 'Just now', due: 'in 10 min', overdue: false, score: 50, note: 'New lead added to the workspace.' }, ...items]);
    this.newLeadName = ''; this.newLeadEmail = ''; this.newLeadCompany = ''; this.showLeadModal.set(false); this.page.set('Leads'); this.notify('New lead added successfully');
  }
  protected notify(message: string): void { this.toast.set(message); setTimeout(() => this.toast.set(''), 2800); }
  protected sourceClass(source: string): string { return source.toLowerCase().replace(' ', '-'); }
}
