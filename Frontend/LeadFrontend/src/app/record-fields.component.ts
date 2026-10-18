import { Component, Input, OnChanges, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CrmApiService } from './crm-api.service';
type Field={id:string;name:string;fieldType:string;options:string[]};
@Component({selector:'app-record-fields',standalone:true,imports:[FormsModule],template:`
@if(fields().length){<section class="workbench work-card"><h3>Custom fields</h3><form class="work-form" (ngSubmit)="save()"><fieldset [disabled]="busy()||!editable">
@for(f of fields();track f.id){<label>{{f.name}}@switch(f.fieldType){@case('Dropdown'){<select [name]="f.id" [(ngModel)]="values[f.id]"><option [ngValue]="null">Choose value</option>@for(option of f.options;track option){<option [value]="option">{{option}}</option>}</select>}@case('Checkbox'){<input type="checkbox" [name]="f.id" [(ngModel)]="values[f.id]">}@case('Number'){<input type="number" [name]="f.id" [(ngModel)]="values[f.id]">}@case('Date'){<input type="date" [name]="f.id" [(ngModel)]="values[f.id]">}@default{<input [name]="f.id" [(ngModel)]="values[f.id]">}}</label>}
@if(editable){<button>Save custom fields</button>}</fieldset></form><p role="status">{{message()}}</p></section>}`})
export class RecordFieldsComponent implements OnChanges {
 @Input({required:true}) recordId='';@Input() entityType='Opportunity';@Input() editable=false;
 fields=signal<Field[]>([]);busy=signal(false);message=signal('');values:Record<string,string|number|boolean|null>={};record:Record<string,unknown>={};
 constructor(private api:CrmApiService){} ngOnChanges(){void this.load();} private path(){return '/api/'+(this.entityType==='Contact'?'contacts':'opportunities')+'/'+this.recordId;}
 async load(){this.busy.set(true);this.message.set('');try{this.record=await this.api.request(this.path());this.values={...(this.record['customFields'] as typeof this.values||{})};this.fields.set(await this.api.request('/api/custom-fields?entityType='+this.entityType+(this.record['pipelineId']?'&pipelineId='+this.record['pipelineId']:'')));}catch(e){this.message.set(String(e));}finally{this.busy.set(false);}}
 async save(){if(this.busy())return;this.busy.set(true);try{this.record=await this.api.mutate(this.path(),'PUT',{...this.record,customFields:this.values});this.message.set('Saved. Refresh the opportunity before moving it to a new stage.');}catch(e){this.message.set(e instanceof Error?e.message:'Save failed.');}finally{this.busy.set(false);}}
}
