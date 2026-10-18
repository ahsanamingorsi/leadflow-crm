import { Component, Input, OnInit, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ApiAssignedLead, CurrentUser, CrmApiService, PageResult } from './crm-api.service';

type Availability = { userId: string; displayName: string; status: string; openLeadCount: number; rowVersion: string };
type Notice = { id: string; title: string; message: string; createdAtUtc: string; readAtUtc?: string; rowVersion: string };
type Intake = { id: string; contactName: string; status: string; lastError?: string; rowVersion: string };

@Component({ selector: 'app-lead-desk', standalone: true, imports: [FormsModule, DatePipe], template: `
<section class="workbench">
  <header><div><h1>Lead desk</h1><p>New enquiries, response deadlines and your availability · Pakistan time</p></div><button (click)="load()" [disabled]="busy()">Refresh</button></header>
  @if(error()) { <p class="work-error" role="alert">{{error()}}</p> } @if(note()) { <p role="status">{{note()}}</p> }
  <fieldset [disabled]="busy()">
  @if(can('leads.availability.edit-own')) { <section class="work-card"><label>My availability<select [ngModel]="mine()?.status || 'Offline'" (ngModelChange)="setAvailability($event)"><option>Available</option><option>Busy</option><option>OnLeave</option><option>Offline</option></select></label><p>Changing to leave or offline lets the assignment service move unattended leads.</p></section> }
  @if(can('leads.create')) { <details class="work-card"><summary>Add a new enquiry</summary><form (ngSubmit)="create()" class="work-form"><label>Name<input name="name" [(ngModel)]="name" required minlength="2" maxlength="160"></label><label>Email<input name="email" [(ngModel)]="email" type="email"></label><label>Phone<input name="phone" [(ngModel)]="phone" placeholder="+923001234567"></label><label>Message<textarea name="message" [(ngModel)]="message" maxlength="4000"></textarea></label><button type="submit">Create and assign lead</button></form></details> }
  <div class="work-toolbar"><input aria-label="Search leads" [(ngModel)]="search" placeholder="Search leads"><select aria-label="Lead status" [(ngModel)]="closed"><option value="false">Open</option><option value="true">Closed</option><option value="">All</option></select><button (click)="page=1; load()">Search</button></div>
  <div class="work-table"><table><thead><tr><th>Contact</th><th>Owner / source</th><th>Acknowledge by</th><th>Actions</th></tr></thead><tbody>
  @for(lead of leads(); track lead.id) { <tr><td><strong>{{lead.contactName}}</strong><small>{{lead.email || lead.phone}}</small></td><td>{{lead.ownerName}}<small>{{lead.source}}</small></td><td>{{lead.acknowledgementDeadlineUtc | date:'dd MMM, HH:mm:ss':'+0500'}}<small [class.work-error]="remaining(lead).startsWith('Overdue')">{{remaining(lead)}}</small></td><td>
  @if(!lead.isClosed && can('leads.acknowledge')) { <button [disabled]="!!lead.acknowledgedAtUtc" (click)="act(lead,'acknowledge')">Acknowledge</button> }
  @if(!lead.isClosed && can('leads.edit-own')) { <button (click)="act(lead,'close')">Close</button> }
  @if(!lead.isClosed && can('leads.reassign')) { <button (click)="selected=lead; owner=lead.ownerUserId">Reassign</button> }
  <button (click)="history(lead)">History</button></td></tr> } @empty { <tr><td colspan="4">No leads in this view.</td></tr> }
  </tbody></table></div><div class="work-toolbar"><button [disabled]="page===1" (click)="page=page-1; load()">Previous</button><span>{{total()}} leads · page {{page}}</span><button [disabled]="page*25>=total()" (click)="page=page+1; load()">Next</button></div>
  @if(selected) { <form class="work-card work-form" (ngSubmit)="reassign()"><h2>Reassign {{selected.contactName}}</h2><label>Employee<select name="owner" [(ngModel)]="owner" required>@for(person of availability(); track person.userId) { <option [value]="person.userId">{{person.displayName}} · {{person.status}} · {{person.openLeadCount}} open</option> }</select></label><label>Reason<input name="reason" [(ngModel)]="reason" required maxlength="400"></label><button type="submit">Apply reassignment</button><button type="button" (click)="selected=null">Cancel</button></form> }
  @if(historyRows().length) { <section class="work-card"><h2>Assignment history</h2>@for(row of historyRows(); track $index) { <p>{{row.assignedAtUtc | date:'dd MMM HH:mm':'+0500'}} · {{row.newOwnerName}} · {{row.reason}}</p> }</section> }
  @if(can('leads.notifications.view')) { <section class="work-card"><h2>My notifications</h2>@for(item of notices(); track item.id) { <article class="work-row"><div><strong>{{item.title}}</strong><p>{{item.message}}</p></div>@if(!item.readAtUtc) { <button (click)="readNotice(item)">Mark read</button> }</article> } @empty { <p>No notifications.</p> }</section> }
  @if(can('assignment.rules.manage')) { <section class="work-card"><h2>Intake recovery</h2>@for(item of intakes(); track item.id) { <article class="work-row"><div><strong>{{item.contactName}} · {{item.status}}</strong><p>{{item.lastError}}</p></div><button (click)="retryIntake(item)">Retry assignment</button></article> } @empty { <p>No intake records waiting for recovery.</p> }</section> }
  </fieldset>
</section>` })
export class LeadDeskComponent implements OnInit, OnDestroy {
  @Input({required:true}) user!: CurrentUser;
  leads = signal<ApiAssignedLead[]>([]); availability = signal<Availability[]>([]); notices = signal<Notice[]>([]); intakes = signal<Intake[]>([]);
  historyRows = signal<Array<{ assignedAtUtc:string; newOwnerName:string; reason:string }>>([]);
  busy=signal(false); error=signal(''); note=signal(''); total=signal(0); now=signal(Date.now());
  page=1; search=''; closed='false'; name=''; email=''; phone=''; message=''; owner=''; reason=''; selected:ApiAssignedLead|null=null;
  private key=crypto.randomUUID(); private tick?: ReturnType<typeof setInterval>; private refresh?:ReturnType<typeof setInterval>;
  constructor(private api:CrmApiService){}
  can(p:string){return this.user.permissions.includes(p);}
  mine(){return this.availability().find(x=>x.userId===this.user.userId);}
  ngOnInit(){void this.load(); this.tick=setInterval(()=>this.now.set(Date.now()),1000); this.refresh=setInterval(()=>{if(!this.busy() && document.visibilityState==='visible') void this.load();},30000);}
  ngOnDestroy(){clearInterval(this.tick);clearInterval(this.refresh);}
  remaining(l:ApiAssignedLead){if(l.isClosed)return 'Closed';if(l.acknowledgedAtUtc)return 'Acknowledged';const s=Math.floor((Date.parse(l.acknowledgementDeadlineUtc)-this.now())/1000);return (s<0?'Overdue · ':'')+Math.floor(Math.abs(s)/60)+'m '+Math.abs(s)%60+'s';}
  async run(work:()=>Promise<void>){if(this.busy())return;this.busy.set(true);this.error.set('');try{await work();}catch(e){this.error.set(e instanceof Error?e.message:'Action failed.');}finally{this.busy.set(false);}}
  async load(){await this.run(()=>this.fetch());}
  private async fetch(){const rows=await this.api.request<PageResult<ApiAssignedLead>>('/api/leads?'+new URLSearchParams({page:String(this.page),pageSize:'25',search:this.search,...(this.closed?{closed:this.closed}:{})}));this.leads.set(rows.items);this.total.set(rows.totalCount);
    this.availability.set(await this.api.request<Availability[]>('/api/workspace/assignment/availability'));
    if(this.can('leads.notifications.view'))this.notices.set((await this.api.request<PageResult<Notice>>('/api/workspace/assignment/my-notifications?pageSize=25')).items);
    if(this.can('assignment.rules.manage'))this.intakes.set((await this.api.request<PageResult<Intake>>('/api/leads/intakes?pageSize=100')).items.filter(x=>x.status!=='Assigned'));
  }
  async create(){await this.run(async()=>{const result=await this.api.mutate<{status:string}>('/api/leads/intake','POST',{contactName:this.name,email:this.email||null,phone:this.phone||null,message:this.message||null,source:'Manual'},{'Idempotency-Key':this.key});this.note.set('Enquiry recorded: '+result.status);this.key=crypto.randomUUID();this.name=this.email=this.phone=this.message='';await this.fetch();});}
  async act(l:ApiAssignedLead,action:string){await this.run(async()=>{await this.api.mutate('/api/leads/'+l.id+'/'+action,'POST',{rowVersion:l.rowVersion});await this.fetch();});}
  async setAvailability(status:string){await this.run(async()=>{await this.api.mutate('/api/workspace/assignment/availability/me','PUT',{status,rowVersion:this.mine()?.rowVersion});await this.fetch();});}
  async reassign(){const l=this.selected;if(!l)return;await this.run(async()=>{await this.api.mutate('/api/leads/'+l.id+'/reassign','PUT',{ownerUserId:this.owner,reason:this.reason,rowVersion:l.rowVersion});this.selected=null;this.reason='';await this.fetch();});}
  async history(l:ApiAssignedLead){await this.run(async()=>this.historyRows.set(await this.api.request('/api/leads/'+l.id+'/assignments')));}
  async readNotice(n:Notice){await this.run(async()=>{await this.api.mutate('/api/workspace/assignment/my-notifications/'+n.id+'/read','POST',{rowVersion:n.rowVersion});await this.fetch();});}
  async retryIntake(n:Intake){await this.run(async()=>{await this.api.mutate('/api/leads/intakes/'+n.id+'/retry','POST',{rowVersion:n.rowVersion});await this.fetch();});}
}
