# Phase 2: CRM backend

Implemented in the existing .NET MVC Web API project using SQL Server and EF Core. Controllers, DTOs, models and the shared CRM service live in the same project.

## Start the API

From `Backend/leadflow-crm.API`:

```powershell
dotnet ef database update -- --environment Development
dotnet run --launch-profile https
```

Open `https://localhost:7178/scalar/`. Sign in with an existing workspace account or register a new one. Scalar's existing configuration supplies the antiforgery header for writes. Other clients must keep the authentication cookies, obtain a fresh token from `GET /api/auth/csrf`, and send it as `X-XSRF-TOKEN` on POST, PUT and DELETE.

Migration `20261018075909_CrmPhaseTwo` creates the CRM tables and gives existing workspaces the same Sales pipeline as new registrations: New, Qualified, Proposal, Won and Lost. Owner and Admin system roles receive `crm.pipelines.manage` and `crm.custom-fields.manage`. Custom roles can receive these permissions through the existing role API.

## Endpoint map

| Resource | Operations |
| --- | --- |
| `/api/companies` | GET paginated list; POST create |
| `/api/companies/{id}` | GET detail; PUT update; DELETE |
| `/api/contacts` | GET paginated list; POST create |
| `/api/contacts/{id}` | GET detail; PUT update; DELETE |
| `/api/opportunities` | GET paginated list; POST create |
| `/api/opportunities/{id}` | GET detail; PUT update; DELETE |
| `/api/opportunities/{id}/move` | POST stage or pipeline move |
| `/api/pipelines` | GET paginated list including stages; POST create |
| `/api/pipelines/{id}` | GET detail; PUT rename; DELETE |
| `/api/pipelines/{pipelineId}/stages` | POST add stage |
| `/api/pipelines/{pipelineId}/stages/{id}` | PUT rename/change classification; DELETE |
| `/api/pipelines/{pipelineId}/stage-order` | PUT complete ordered stage list |
| `/api/pipelines/{pipelineId}/stages/{id}/required-fields` | PUT required custom field IDs |
| `/api/custom-fields?entityType=Opportunity&pipelineId={id}` | GET matching shared and pipeline fields |
| `/api/custom-fields` | POST definition |
| `/api/custom-fields/{id}` | PUT name/dropdown options; DELETE unused definition |
| `/api/contacts/{id}/activities` | GET contact history and permitted related opportunity history |
| `/api/opportunities/{id}/activities` | GET opportunity history |
| `/api/contacts/{id}/notes` | POST note |
| `/api/opportunities/{id}/notes` | POST note |
| `/api/audit` | GET paginated audit history with event, actor and date filters |

For a contact's related opportunities, request `/api/opportunities?contactId={id}`. A contact's `companyId` links to `/api/companies/{id}`; `/api/contacts?companyId={id}` lists a company's contacts. Each endpoint checks its own permission, including related activity retrieval.

Lists return `{ items, totalCount, page, pageSize }`. The default page size is 25 and maximum is 100. All lists support `search`. Contacts support company, owner and creation-date filters; opportunities additionally support contact, pipeline and stage filters. Creation date ranges use `createdFromUtc` inclusive and `createdToUtc` exclusive. Audit filters are `eventType`, `actorUserId`, `fromUtc` and `toUtc`.

## First CRM record in Scalar

1. Use `GET /api/auth/me` for your `userId`.
2. Use `GET /api/pipelines` and copy the default pipeline ID and its New stage ID.
3. Create a company using `POST /api/companies`:

```json
{
  "name": "Demo Solar",
  "email": "sales@example.com",
  "phone": "+923001234567",
  "customFields": {}
}
```

4. Create a contact using `POST /api/contacts`, replacing the placeholders with returned UUIDs:

```json
{
  "displayName": "Demo Customer",
  "companyId": "COMPANY-ID",
  "ownerUserId": "USER-ID",
  "email": "customer@example.com",
  "phone": "+923009876543",
  "optedOut": false,
  "customFields": {}
}
```

Company and contact owner links are optional; an opportunity requires an active owner and an existing contact. A company is a customer organization within a workspace.

5. Create an opportunity using `POST /api/opportunities`:

```json
{
  "title": "Home solar installation",
  "contactId": "CONTACT-ID",
  "pipelineId": "PIPELINE-ID",
  "stageId": "NEW-STAGE-ID",
  "ownerUserId": "USER-ID",
  "value": 125000.50,
  "expectedCloseDate": "2026-11-01",
  "description": "Customer requested a quotation.",
  "customFields": {}
}
```

Currency is PKR. Amounts use `decimal(18,2)` and reject negative values or more than two decimal places. Event timestamps are stored as UTC and returned with `Z`; clients should display them using the workspace's `Asia/Karachi` time zone. `expectedCloseDate` and custom Date fields are calendar dates with no time component.

## Custom fields and required stages

Definitions use `entityType` = `Company`, `Contact` or `Opportunity`; `fieldType` = `Text`, `Number`, `Date`, `Dropdown` or `Checkbox`. `pipelineId` is optional and allowed only for Opportunity definitions. Without a pipeline ID, an Opportunity field is shared across pipelines. Contact data remains shared when opportunities use different pipelines.

Create a pipeline field with `POST /api/custom-fields`:

```json
{
  "name": "Customer requirement",
  "entityType": "Opportunity",
  "pipelineId": "PIPELINE-ID",
  "fieldType": "Text",
  "options": []
}
```

Dropdowns require unique non-empty `options`, for example `["Solar", "Battery"]`. Field values use the returned field ID as their JSON key:

```json
{
  "CUSTOM-FIELD-ID": "A 5 kW system"
}
```

Number values must be JSON numbers, Checkbox values must be JSON booleans, and Date values must use `yyyy-MM-dd`. A required Checkbox accepts both `true` and `false`; the requirement means a value must be supplied. Null clears an optional custom value.

To require a field at a stage, PUT to `/api/pipelines/{pipelineId}/stages/{id}/required-fields`:

```json
{
  "fieldIds": ["CUSTOM-FIELD-ID"],
  "rowVersion": "LATEST-STAGE-VERSION"
}
```

The API refuses a new requirement if existing opportunities in that stage have missing values. Fill those records before enabling the requirement. Creation, normal updates and moves all enforce stage requirements.

Move an opportunity using `POST /api/opportunities/{id}/move`:

```json
{
  "stageId": "DESTINATION-STAGE-ID",
  "rowVersion": "LATEST-OPPORTUNITY-VERSION",
  "customFields": {
    "CUSTOM-FIELD-ID": "A 5 kW system"
  }
}
```

Omitting `customFields` on a move preserves existing values. Supplying it replaces the full set. A move to another pipeline must supply values valid for that pipeline; incompatible values are rejected so they cannot disappear silently.

## Editing, permissions and deletion

- Every PUT requires the latest base64 `rowVersion` returned by GET or a previous write. A stale version returns 409. After adding a note, reload its parent record before another edit because the note updates the parent's modification time and version.
- DELETE requires `If-Match` containing that base64 version; surrounding quotes are accepted. Missing or malformed versions return 400.
- Company, Contact and Opportunity PUT requests replace editable fields and the custom-field dictionary. Include values you want to retain. The exception is Opportunity `value`: omitting it preserves the stored amount; on creation it defaults to zero.
- Opportunity values are null with `valueHidden: true` without `crm.opportunities.value.view`. That permission is also required to set the amount. Audit responses remove the amount for viewers without this permission.
- Pipeline creation starts an empty pipeline; add stages before creating opportunities. New stages append to the end. Reordering requires every stage exactly once, each with its current version.
- A populated stage cannot be deleted or reclassified as Open/Won/Lost until its opportunities are moved. The default pipeline cannot be deleted, and it must retain at least one stage.
- Company deletion is blocked by active contacts; contact deletion is blocked by active opportunities. A custom pipeline must have its stages, custom fields and opportunities removed first.
- CRM deletes mark records deleted and retain their audit/activity history. Normal CRM reads exclude deleted records.
- A field's type and scope are immutable. A field used by retained records or stage requirements cannot be deleted; dropdown options in use cannot be removed. Renaming a field preserves its values.
- Record changes and their audit/activity entries are saved together. SQL row versions reject conflicting updates, and write transactions protect dependency checks during deletion/configuration changes.
- Workspace IDs come from the authenticated user. Composite database relationships prevent contacts, owners, stages and other linked data from crossing workspace boundaries.
- Audit history is read-only through the API. Notes and automatic activity entries show who acted and when. Automatic activity summaries list changed fields without disclosing financial values.

## Verification

The integration script uses Python's standard library, a running API and `sqlcmd` with Windows authentication:

Verified on 2026-10-18: build succeeded; the migration was applied to local SQL Express; EF reported no pending model changes; 220 HTTP/behavior checks passed and their fixtures were removed. OpenAPI exposes 35 CRM operations across 20 paths, and Scalar returned HTTP 200. The existing transitive `System.Security.Cryptography.Pkcs 9.0.11` package still produces NU1902/NU1903 warnings; this phase did not change dependencies.

```powershell
python Backend/tests/phase2_smoke.py --server 'AHSAN-DESKTOP\SQLEXPRESS'
```

It creates temporary workspaces with random names and accounts, exercises the API, then removes only those exact fixtures. It checks workspace isolation, permissions, hidden values, custom field types, required stages, concurrent edits, relationships, configuration changes, activity/audit history and deletion behavior. SQL cleanup must run with the same normal Windows database access as the API. If cleanup fails, the script prints exact retry arguments.

The Phase 2 scope is CRM records and configuration APIs. Saved lists, exports, lead assignment/escalation, external integrations and Angular screens remain later work.
