import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CrmApiService } from './crm-api.service';
@Component({selector:'app-invitation-join',standalone:true,imports:[FormsModule],template:`
<details class="workbench work-card"><summary>Accept invitation</summary><form class="work-form" (ngSubmit)="join()"><label>Invitation token<input name="token" [(ngModel)]="token" required autocomplete="off"></label><label>Your name<input name="displayName" [(ngModel)]="displayName" required minlength="2" maxlength="120"></label><label>New password<input name="password" type="password" [(ngModel)]="password" required minlength="12" autocomplete="new-password"></label><button [disabled]="busy()">Join workspace</button><p role="status">{{message()}}</p></form></details>`})
export class InvitationJoinComponent {
 token='';displayName='';password='';busy=signal(false);message=signal('');
 constructor(private api:CrmApiService){}
 async join(){if(this.busy())return;this.busy.set(true);try{await this.api.mutate('/api/workspace/invitations/accept','POST',{token:this.token,displayName:this.displayName,password:this.password});this.password='';location.reload();}catch(e){this.message.set(e instanceof Error?e.message:'Could not accept invitation.');}finally{this.busy.set(false);}}
}
