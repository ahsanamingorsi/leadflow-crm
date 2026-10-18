import { Component, Input, OnInit, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ApiContact, CrmApiService, CurrentUser, PageResult } from './crm-api.service';
type Message={id:string;contactId?:string;phone:string;body:string;direction:string;status:string;contentSid?:string;lastError?:string;createdAtUtc:string};
type TwilioConfig={configured:boolean;accountSid?:string;fromNumber?:string;publicBaseUrl?:string;inboundUrl?:string};

@Component({selector:'app-messaging',standalone:true,imports:[FormsModule,DatePipe],template:`
<section class="workbench">
 @if(error()){<p class="work-error" role="alert">{{error()}}</p>} @if(note()){<p role="status">{{note()}}</p>}
 <fieldset [disabled]="busy()">
 @if(mode==='settings') {
 <form class="work-card work-form" (ngSubmit)="saveSettings()"><h2>Twilio WhatsApp</h2><p>{{config()?.configured?'Credentials saved. A successful webhook confirms connectivity.':'Connect a Twilio WhatsApp sandbox or approved sender.'}}</p><label>Account SID<input name="sid" [(ngModel)]="accountSid" required pattern="AC[0-9a-fA-F]{32}"></label><label>Auth Token<input name="token" [(ngModel)]="authToken" type="password" autocomplete="new-password" [required]="!config()?.configured" placeholder="Leave blank to keep saved token"></label><label>WhatsApp sender<input name="from" [(ngModel)]="fromNumber" required placeholder="+14155238886"></label><label>Public HTTPS API origin<input name="url" [(ngModel)]="publicBaseUrl" type="url" required placeholder="https://your-api.example.com"></label><button>Save WhatsApp settings</button>@if(config()?.inboundUrl){<label>Twilio inbound webhook · POST<input readonly [value]="config()?.inboundUrl"></label><p>Paste this into the sender or sandbox inbound webhook field. Delivery callbacks are attached to each outgoing message automatically.</p>}</form>
 } @else {
 <header><div><h2>{{mode==='email'?'Write an email':'WhatsApp inbox'}}</h2><p>{{mode==='email'?'Start a conversation with a CRM contact.':'Replies, delivery status and customer consent.'}}</p></div><button type="button" (click)="load()">Refresh</button></header>
 @if(can(mode==='email'?'email.inbox.reply':'whatsapp.inbox.reply')) {
 <form class="work-card work-form" (ngSubmit)="send()"><label>Contact<select name="contact" [(ngModel)]="contactId" (ngModelChange)="selectContact()" required><option value="">Choose contact</option>@for(c of contacts();track c.id){<option [value]="c.id">{{c.displayName}} · {{mode==='email'?c.email:c.phone}}</option>}</select></label>
 @if(contactId&&can('crm.contacts.edit')){<div class="work-toolbar"><span>{{optedOut?'Contact has opted out':'Contact can receive communication'}}</span><button type="button" (click)="setOptOut(!optedOut)">{{optedOut?'Record renewed consent':'Mark opted out'}}</button></div>}
 @if(mode==='email'){<label>Subject<input name="subject" [(ngModel)]="subject" (ngModelChange)="newDraft()" required maxlength="490"></label>}
 <label>Message<textarea name="body" [(ngModel)]="body" (ngModelChange)="newDraft()" [required]="mode==='email'||!contentSid" [maxlength]="mode==='email'?10000:4096" rows="5"></textarea></label>
 @if(mode!=='email'){<details><summary>Approved template (outside 24-hour reply window)</summary><label>Content SID<input name="contentSid" [(ngModel)]="contentSid" (ngModelChange)="newDraft()" placeholder="HX…"></label><label><input name="consent" type="checkbox" [(ngModel)]="consent">I have recorded this customer's consent to receive WhatsApp messages.</label><p>This form supports approved templates without variables. Free text replies require a customer message within the last 24 hours.</p></details>}
 <button [disabled]="optedOut||!contactId">{{mode==='email'?'Send email':'Send WhatsApp message'}}</button></form> }
 @if(mode!=='email') { <div class="work-toolbar"><button (click)="contactId='';page=1;loadMessages()">All visible messages</button></div><section class="work-card">@for(m of messages();track m.id){<article class="message-entry" [class.message-outbound]="m.direction==='Outbound'"><div><strong>{{m.phone}} · {{m.direction}}</strong><small>{{m.createdAtUtc | date:'dd MMM yyyy, HH:mm':'+0500'}} PKT · {{m.status}}</small></div><p>{{m.body || 'Approved template'}}</p>@if(m.lastError){<p class="work-error">{{m.lastError}}</p>}@if((m.status==='failed'||m.status==='undelivered')&&can('whatsapp.inbox.reply')){<button (click)="retryDraft(m)">Prepare retry</button>}</article>} @empty {<p>No WhatsApp messages in this view.</p>}</section><div class="work-toolbar"><button [disabled]="page===1" (click)="page=page-1;loadMessages()">Previous</button><span>Page {{page}} · {{total()}} messages</span><button [disabled]="page*30>=total()" (click)="page=page+1;loadMessages()">Next</button></div> }
 }
 </fieldset>
</section>`})
export class MessagingComponent implements OnInit, OnDestroy {
 @Input({required:true}) user!:CurrentUser;@Input() mode='whatsapp';
 busy=signal(false);error=signal('');note=signal('');contacts=signal<ApiContact[]>([]);messages=signal<Message[]>([]);total=signal(0);config=signal<TwilioConfig|null>(null);
 contactId='';body='';subject='';contentSid='';consent=false;optedOut=false;page=1;accountSid='';authToken='';fromNumber='';publicBaseUrl='';
 private key=crypto.randomUUID();private refresh?:ReturnType<typeof setInterval>;
 constructor(private api:CrmApiService){} can(p:string){return this.user.permissions.includes(p);}
 ngOnInit(){void this.load();if(this.mode==='whatsapp')this.refresh=setInterval(()=>{if(!this.busy()&&document.visibilityState==='visible')void this.loadMessages();},15000);}
 ngOnDestroy(){clearInterval(this.refresh);}
 newDraft(){this.key=crypto.randomUUID();}
 async run(work:()=>Promise<void>){if(this.busy())return;this.busy.set(true);this.error.set('');try{await work();}catch(e){this.error.set(e instanceof Error?e.message:'Request failed.');}finally{this.busy.set(false);}}
 async load(){await this.run(async()=>{if(this.mode==='settings'){const c=await this.api.request<TwilioConfig>('/api/integrations/twilio');this.config.set(c);this.accountSid=c.accountSid||'';this.fromNumber=c.fromNumber||'';this.publicBaseUrl=c.publicBaseUrl||'';}else{if(this.can('crm.contacts.view'))this.contacts.set((await this.api.contacts()).items);if(this.mode==='whatsapp')await this.fetchMessages();}});}
 private async fetchMessages(){const p=await this.api.request<PageResult<Message>>('/api/whatsapp/messages?page='+this.page+'&pageSize=30'+(this.contactId?'&contactId='+this.contactId:''));this.messages.set(p.items);this.total.set(p.totalCount);}
 async loadMessages(){await this.run(()=>this.fetchMessages());}
 async selectContact(){this.newDraft();await this.run(async()=>{if(this.contactId){const c=await this.api.request<{optedOut:boolean}>('/api/contacts/'+this.contactId);this.optedOut=c.optedOut;}else this.optedOut=false;if(this.mode==='whatsapp'){this.page=1;await this.fetchMessages();}});}
 async send(){await this.run(async()=>{if(this.mode==='email'){await this.api.mutate('/api/email/conversations','POST',{contactId:this.contactId,subject:this.subject,bodyText:this.body,clientKey:this.key});this.note.set('Email conversation created. Refresh the email inbox below to check delivery.');this.body=this.subject='';this.newDraft();}
 else{const m=await this.api.mutate<Message>('/api/whatsapp/messages','POST',{contactId:this.contactId,body:this.body,contentSid:this.contentSid||null,confirmedOptIn:this.consent,clientKey:this.key});this.note.set('WhatsApp status: '+m.status);if(m.status!=='unknown'&&m.status!=='pending'){this.body='';this.newDraft();}await this.fetchMessages();}});}
 retryDraft(m:Message){this.contactId=m.contactId||'';this.body=m.body;this.contentSid=m.contentSid||'';this.consent=false;this.newDraft();this.note.set('Review this message and press Send to retry.');void this.selectContact();}
 async setOptOut(value:boolean){if(!value&&!confirm('Has the customer explicitly renewed consent?'))return;await this.run(async()=>{const c=await this.api.request<Record<string,unknown>>('/api/contacts/'+this.contactId);await this.api.mutate('/api/contacts/'+this.contactId,'PUT',{...c,optedOut:value});this.optedOut=value;this.note.set(value?'Contact opted out.':'Customer consent recorded.');});}
 async saveSettings(){await this.run(async()=>{this.config.set(await this.api.mutate('/api/integrations/twilio','PUT',{accountSid:this.accountSid,authToken:this.authToken,fromNumber:this.fromNumber,publicBaseUrl:this.publicBaseUrl}));this.authToken='';this.note.set('Twilio settings saved. Configure the inbound webhook in your Twilio console.');});}
}
