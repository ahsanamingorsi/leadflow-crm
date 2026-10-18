# Phase 5: Dashboard, saved lists, and customer history

## Delivered

- Added `GET /api/dashboard/overview` for Pakistan-time lead and payment metrics, open pipeline totals, first-response duration, recovery and escalation counts, employee availability, and upcoming appointments. Workspace-wide results depend on the caller's role; restricted opportunity and payment values are omitted.
- Added workspace-scoped saved lists for contacts, opportunities, and leads. Lists support search, owner, created-date range, lead source, opportunity pipeline and stage, and supported custom-field filters. Shared lists are read-only for other users. Changes and deletions are audited.
- Extended audit search with event, record type, actor, date, and text filters.
- Added audit/activity records for email activity, payment events, calendar events, and email integration changes. Sensitive integration secrets are not included in audit payloads.
- Connected the Angular Overview, Reports, Recovery queue, Contacts customer history, Saved lists, and Audit history screens to the API. Dashboard card visibility is stored per user in browser storage. Saved-list definitions persist in SQL Server.
- Kept email as the messaging channel. Twilio/WhatsApp was not enabled.

## Database

EF migration `20261018094902_PhaseFiveDashboardSavedLists` creates the `SavedLists` table, workspace and owner foreign keys, row-version concurrency support, and lookup indexes.

The migration was generated but could not be applied from the Codex execution session. SQL Server Express is running on the current host, but Windows integrated authentication from this session failed with an SSPI principal error. Run the EF database update from Visual Studio or a terminal under the Windows account that can already connect to `LeadFlowCrmDb`.

After applying the migration, confirm `SavedLists` and `__EFMigrationsHistory` contain the Phase 5 migration. The application does not automatically run migrations at startup.

## Verification

- .NET API build: succeeded. Existing package vulnerability warnings remain for `System.Security.Cryptography.Pkcs` 9.0.11.
- Angular production build: succeeded.
- Tests and live provider flows were not run. Provider credentials are not configured in this environment.

The rubric's real phone messaging criterion remains unmet because the requested messaging channel is email only.
